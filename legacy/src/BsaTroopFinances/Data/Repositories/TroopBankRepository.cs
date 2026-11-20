using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class TroopBankRepository(FinanceDb db) : ITroopBankRepository
{
    public async Task<IReadOnlyList<TroopAccountEntity>> GetAccountsAsync() =>
        await db.ReadAsync(async c => await c.TroopAccounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync());

    public async Task<TroopAccountEntity?> GetAccountAsync(string accountId) =>
        await db.ReadAsync(c => c.TroopAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.PartitionKey == TroopAccountEntity.PartitionKeyValue && a.RowKey == accountId));

    public Task AddAccountAsync(TroopAccountEntity account) => db.WriteAsync(c =>
    {
        c.TroopAccounts.Add(account);
        return Task.CompletedTask;
    });

    public Task UpdateAccountAsync(TroopAccountEntity account) => db.WriteAsync(c => c.UpdateDetachedAsync(account));

    public Task DeleteAccountAsync(string accountId) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<TroopAccountEntity>(TroopAccountEntity.PartitionKeyValue, accountId));

    public async Task<IReadOnlyList<TroopTransactionEntity>> GetTransactionsAsync(string accountId) =>
        await db.ReadAsync(async c => await c.TroopTransactions.AsNoTracking()
            .Where(t => t.PartitionKey == accountId).OrderBy(t => t.Date).ThenBy(t => t.RowKey).ToListAsync());

    public async Task<IReadOnlyList<TroopTransactionEntity>> GetAllTransactionsAsync() =>
        await db.ReadAsync(async c => await c.TroopTransactions.AsNoTracking().OrderBy(t => t.Date).ThenBy(t => t.RowKey).ToListAsync());

    public async Task<TroopTransactionEntity?> GetTransactionAsync(string accountId, string rowKey) =>
        await db.ReadAsync(c => c.TroopTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.PartitionKey == accountId && t.RowKey == rowKey));

    public async Task<IReadOnlyList<TroopTransactionEntity>> GetTransactionsForSourceAsync(string sourceType, string sourceId) =>
        await db.ReadAsync(async c => await c.TroopTransactions.AsNoTracking()
            .Where(t => t.SourceType == sourceType && t.SourceId == sourceId).ToListAsync());

    public Task AddTransactionAsync(TroopTransactionEntity transaction) => db.WriteAsync(c =>
    {
        c.TroopTransactions.Add(transaction);
        return Task.CompletedTask;
    });

    public Task UpdateTransactionAsync(TroopTransactionEntity transaction) => db.WriteAsync(c => c.UpdateDetachedAsync(transaction));

    public Task DeleteTransactionAsync(string accountId, string rowKey) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<TroopTransactionEntity>(accountId, rowKey));

    public async Task<IReadOnlyList<DepositEntity>> GetDepositsAsync() =>
        await db.ReadAsync(async c => await c.Deposits.AsNoTracking().OrderByDescending(d => d.Date).ThenByDescending(d => d.CreatedUtc).ToListAsync());

    public async Task<DepositEntity?> GetDepositAsync(string depositId) =>
        await db.ReadAsync(c => c.Deposits.AsNoTracking()
            .FirstOrDefaultAsync(d => d.PartitionKey == DepositEntity.PartitionKeyValue && d.RowKey == depositId));

    public async Task<IReadOnlyList<DepositLineEntity>> GetDepositLinesAsync(string depositId) =>
        await db.ReadAsync(async c => await c.DepositLines.AsNoTracking().Where(l => l.PartitionKey == depositId).OrderBy(l => l.RowKey).ToListAsync());

    public async Task<IReadOnlySet<(string Kind, string SourceId)>> GetDepositedSourcesAsync()
    {
        var sources = await db.ReadAsync(async c => await c.DepositLines.AsNoTracking()
            .Where(l => l.Kind != DepositLineKind.Other)
            .Select(l => new { l.Kind, l.SourceId }).ToListAsync());
        return sources.Select(s => (s.Kind, s.SourceId)).ToHashSet();
    }

    public Task AddDepositAsync(DepositEntity deposit, IReadOnlyList<DepositLineEntity> lines) => db.WriteAsync(c =>
    {
        c.Deposits.Add(deposit);
        c.DepositLines.AddRange(lines);
        return Task.CompletedTask;
    });

    public Task DeleteDepositAsync(string depositId) => db.WriteAsync(async c =>
    {
        await c.DepositLines.Where(l => l.PartitionKey == depositId).ExecuteDeleteAsync();
        await c.DeleteByKeyAsync<DepositEntity>(DepositEntity.PartitionKeyValue, depositId);
    });

    public async Task<IReadOnlyList<ReconciliationEntity>> GetReconciliationsAsync(string accountId) =>
        await db.ReadAsync(async c => await c.Reconciliations.AsNoTracking()
            .Where(r => r.PartitionKey == accountId).OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.RowKey).ToListAsync());

    public async Task<ReconciliationEntity?> GetReconciliationAsync(string accountId, string reconciliationId) =>
        await db.ReadAsync(c => c.Reconciliations.AsNoTracking().FirstOrDefaultAsync(r => r.PartitionKey == accountId && r.RowKey == reconciliationId));

    public Task AddReconciliationAsync(ReconciliationEntity reconciliation) => db.WriteAsync(c =>
    {
        c.Reconciliations.Add(reconciliation);
        return Task.CompletedTask;
    });

    public Task UpdateReconciliationAsync(ReconciliationEntity reconciliation) => db.WriteAsync(c => c.UpdateDetachedAsync(reconciliation));

    public Task DeleteReconciliationAsync(string accountId, string reconciliationId) =>
        db.WriteAsync(c => c.DeleteByKeyAsync<ReconciliationEntity>(accountId, reconciliationId));
}
