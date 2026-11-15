using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data;

/// <summary>Runs a group of repository calls as one all-or-nothing database transaction.</summary>
public interface IUnitOfWork
{
    Task InTransactionAsync(Func<Task> work);

    Task<T> InTransactionAsync<T>(Func<Task<T>> work);
}

/// <summary>
/// How repositories reach the database. Outside a transaction, each call gets its own short-lived
/// <see cref="FinanceDbContext"/> (the recommended pattern for Blazor Server, where one long-lived
/// context per circuit leads to stale data and "a second operation was started" errors). Inside
/// <see cref="InTransactionAsync"/>, every repository call made from that work shares one context
/// and transaction -- carried in an AsyncLocal, so it reaches the repositories without being
/// passed through every method -- and nothing is committed unless all of it succeeds.
/// </summary>
public sealed class FinanceDb(IDbContextFactory<FinanceDbContext> factory) : IUnitOfWork
{
    private static readonly AsyncLocal<FinanceDbContext?> Ambient = new();

    public async Task<T> ReadAsync<T>(Func<FinanceDbContext, Task<T>> query)
    {
        if (Ambient.Value is { } current)
        {
            return await query(current);
        }

        await using var context = await factory.CreateDbContextAsync();
        return await query(context);
    }

    /// <summary>Applies changes and saves them (inside the current transaction, if there is one).</summary>
    public async Task WriteAsync(Func<FinanceDbContext, Task> change)
    {
        if (Ambient.Value is { } current)
        {
            await change(current);
            await current.SaveChangesAsync();
            return;
        }

        await using var context = await factory.CreateDbContextAsync();
        await change(context);
        await context.SaveChangesAsync();
    }

    public async Task InTransactionAsync(Func<Task> work) =>
        await InTransactionAsync(async () =>
        {
            await work();
            return true;
        });

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        if (Ambient.Value is not null)
        {
            // Already inside a transaction: join it rather than nesting.
            return await work();
        }

        await using var context = await factory.CreateDbContextAsync();
        // With connection resiliency on, a user transaction must run inside the execution strategy
        // so a transient failure retries the whole unit from the start, never half of it.
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync();
            Ambient.Value = context;
            try
            {
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            finally
            {
                Ambient.Value = null;
            }
        });
    }
}

/// <summary>Helpers shared by the repositories.</summary>
public static class FinanceDbContextExtensions
{
    /// <summary>
    /// Updates a detached entity (one read earlier, possibly edited on a page). Its row version is
    /// checked, like the old ETag, so a stale copy throws instead of overwriting newer changes. A
    /// copy built from scratch with no version just overwrites. Any other tracked instance with the
    /// same key in this context is let go first.
    /// </summary>
    public static async Task UpdateDetachedAsync<T>(this FinanceDbContext context, T entity) where T : class
    {
        var entry = context.Entry(entity);
        var key = context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var keyValues = key.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

        foreach (var tracked in context.ChangeTracker.Entries<T>().Where(e => !ReferenceEquals(e.Entity, entity)).ToList())
        {
            if (key.Properties.Select(p => tracked.Property(p.Name).CurrentValue).SequenceEqual(keyValues))
            {
                tracked.State = EntityState.Detached;
            }
        }

        context.Update(entity);
        var version = entry.Property("Version");
        if (version.CurrentValue is null)
        {
            var current = await context.Set<T>().AsNoTracking()
                .Where(e => EF.Property<string>(e, "PartitionKey") == (string)keyValues[0]! && EF.Property<string>(e, "RowKey") == (string)keyValues[1]!)
                .Select(e => EF.Property<byte[]>(e, "Version"))
                .FirstOrDefaultAsync();
            version.OriginalValue = current;
        }
    }

    /// <summary>Removes a row by key whether or not this context is tracking it.</summary>
    public static async Task<int> DeleteByKeyAsync<T>(this FinanceDbContext context, string partitionKey, string rowKey) where T : class =>
        await context.Set<T>()
            .Where(e => EF.Property<string>(e, "PartitionKey") == partitionKey && EF.Property<string>(e, "RowKey") == rowKey)
            .ExecuteDeleteAsync();
}
