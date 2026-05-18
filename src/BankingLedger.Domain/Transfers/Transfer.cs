namespace BankingLedger.Domain.Transfers;

/// Records a money movement from one account to another.
/// Starts as Pending, then reaches a terminal state: Completed, Failed, or RolledBack.
///
/// ACID relevance:
///   Atomicity — the Transfer record, both ledger entries, and both balance updates are all
///               written in a single transaction. Either all of them commit, or none do.
///   Durability — a Completed transfer record is proof the money actually moved.
public class Transfer
{
    private Transfer() { }

    public Guid Id { get; private set; }

    public Guid FromAccountId { get; private set; }

    public Guid ToAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public TransferStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

/// Populated when the transfer ends in a Failed or RolledBack state.</summary>
    public string? FailureReason { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────────────────────────

    public static Transfer Create(Guid fromAccountId, Guid toAccountId, decimal amount)
    {
        return new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = amount,

            // Status starts as Pending — a record of intent before all steps are done.
            // If the transaction commits successfully, we call Complete().
            // If something goes wrong, the transaction rolls back and this record
            // is never persisted (or gets marked RolledBack/Failed in demo scenarios).
            Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    // ── State transitions ────────────────────────────────────────────────────────────────────────

    public void Complete() => Status = TransferStatus.Completed;

    public void Fail(string reason)
    {
        Status = TransferStatus.Failed;
        FailureReason = reason;
    }

    public void MarkRolledBack(string reason)
    {
        Status = TransferStatus.RolledBack;
        FailureReason = reason;
    }
}
