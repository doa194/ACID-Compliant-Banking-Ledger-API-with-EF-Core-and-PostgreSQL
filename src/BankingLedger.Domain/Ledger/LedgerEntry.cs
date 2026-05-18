namespace BankingLedger.Domain.Ledger;


/// An immutable record of every balance change that ever happened to an account.
/// This is the audit trail — once created inside a transaction, it should never be modified.
///
/// ACID relevance:
///   Atomicity  — ledger entries are always created in the same transaction as the balance
///                change. If the transaction rolls back, both the balance change AND the
///                ledger entry are undone together. You will never see an entry for a
///                transaction that did not actually complete.
///   Durability — once committed, the entry survives restarts and proves the transaction happened.

public class LedgerEntry
{
    // EF Core requires a private parameterless constructor to materialise entities from query results.
    private LedgerEntry() { }

    public Guid Id { get; private set; }

    /// The account this entry belongs to.
    public Guid AccountId { get; private set; }

    /// Set when the entry is the debit or credit half of a transfer.
    public Guid? TransferId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    /// The absolute value of money that moved (always positive).
    public decimal Amount { get; private set; }

    
    /// The account balance AFTER this entry was applied.
    /// Lets you reconstruct the full balance history without re-summing all entries.
    
    public decimal BalanceAfterTransaction { get; private set; }

    public string Description { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    // ── Static factories ─────────────────────────────────────────────────────────────────────────

    public static LedgerEntry CreateDeposit(Guid accountId, decimal amount, decimal balanceAfter, string description) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Deposit,
            Amount = amount,
            BalanceAfterTransaction = balanceAfter,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

    public static LedgerEntry CreateWithdrawal(Guid accountId, decimal amount, decimal balanceAfter, string description) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Withdrawal,
            Amount = amount,
            BalanceAfterTransaction = balanceAfter,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

    // A transfer always creates BOTH a debit entry (sender) and a credit entry (receiver).
    // This double-entry approach gives a complete, self-balancing audit trail.

    public static LedgerEntry CreateTransferDebit(
        Guid accountId, Guid transferId, decimal amount, decimal balanceAfter, string description) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TransferId = transferId,
            Type = LedgerEntryType.TransferDebit,
            Amount = amount,
            BalanceAfterTransaction = balanceAfter,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

    public static LedgerEntry CreateTransferCredit(
        Guid accountId, Guid transferId, decimal amount, decimal balanceAfter, string description) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TransferId = transferId,
            Type = LedgerEntryType.TransferCredit,
            Amount = amount,
            BalanceAfterTransaction = balanceAfter,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };
}
