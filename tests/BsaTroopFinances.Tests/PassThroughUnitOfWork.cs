using BsaTroopFinances.Data;

namespace BsaTroopFinances.Tests;

/// <summary>For tests with faked repositories: runs the work with no real transaction. Rollback
/// behavior is covered against a real database in SqlTransactionTests.</summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    public Task InTransactionAsync(Func<Task> work) => work();

    public Task<T> InTransactionAsync<T>(Func<Task<T>> work) => work();
}
