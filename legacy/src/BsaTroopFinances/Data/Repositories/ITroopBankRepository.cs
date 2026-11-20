using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

/// <summary>The troop's own money: its accounts, their registers, deposits and reconciliations.
/// Changes go through ITroopAccountService, which keeps these consistent with each other.</summary>
public interface ITroopBankRepository
{
    Task<IReadOnlyList<TroopAccountEntity>> GetAccountsAsync();
    Task<TroopAccountEntity?> GetAccountAsync(string accountId);
    Task AddAccountAsync(TroopAccountEntity account);
    Task UpdateAccountAsync(TroopAccountEntity account);
    Task DeleteAccountAsync(string accountId);

    /// <summary>An account's register, oldest first (by date, then when entered).</summary>
    Task<IReadOnlyList<TroopTransactionEntity>> GetTransactionsAsync(string accountId);

    /// <summary>Every account's entries -- for balances across accounts.</summary>
    Task<IReadOnlyList<TroopTransactionEntity>> GetAllTransactionsAsync();

    Task<TroopTransactionEntity?> GetTransactionAsync(string accountId, string rowKey);

    /// <summary>The entries a deposit, reimbursement or transfer made.</summary>
    Task<IReadOnlyList<TroopTransactionEntity>> GetTransactionsForSourceAsync(string sourceType, string sourceId);

    Task AddTransactionAsync(TroopTransactionEntity transaction);
    Task UpdateTransactionAsync(TroopTransactionEntity transaction);
    Task DeleteTransactionAsync(string accountId, string rowKey);

    /// <summary>Every deposit, newest first.</summary>
    Task<IReadOnlyList<DepositEntity>> GetDepositsAsync();
    Task<DepositEntity?> GetDepositAsync(string depositId);
    Task<IReadOnlyList<DepositLineEntity>> GetDepositLinesAsync(string depositId);

    /// <summary>The SourceIds of every deposited invoice payment and donation, by line kind.</summary>
    Task<IReadOnlySet<(string Kind, string SourceId)>> GetDepositedSourcesAsync();

    Task AddDepositAsync(DepositEntity deposit, IReadOnlyList<DepositLineEntity> lines);
    Task DeleteDepositAsync(string depositId);

    /// <summary>An account's reconciliations, newest first.</summary>
    Task<IReadOnlyList<ReconciliationEntity>> GetReconciliationsAsync(string accountId);
    Task<ReconciliationEntity?> GetReconciliationAsync(string accountId, string reconciliationId);
    Task AddReconciliationAsync(ReconciliationEntity reconciliation);
    Task UpdateReconciliationAsync(ReconciliationEntity reconciliation);
    Task DeleteReconciliationAsync(string accountId, string reconciliationId);
}
