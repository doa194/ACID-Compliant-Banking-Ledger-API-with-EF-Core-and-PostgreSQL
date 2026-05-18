using BankingLedger.Domain.Exceptions;

namespace BankingLedger.Domain.Accounts;


/// The core aggregate root representing a bank account.
///
/// ACID relevance:
///   Atomicity   — balance changes are always paired with ledger entries inside one transaction.
///   Consistency — domain methods throw before writing if business rules would be violated.
///   Isolation   — the xmin system column (via EF Core shadow property) is used as a
///                 concurrency token: if two transactions modify the same account
///                 simultaneously, EF Core detects the conflict and throws
///                 DbUpdateConcurrencyException. FOR UPDATE row-locking is also used in
///                 withdrawal and transfer flows.
///   Durability  — once SaveChangesAsync + CommitAsync return, the new balance survives restarts.
public class Account
{
    // Private parameterless constructor: EF Core needs this to materialise entities from the database.
    // We hide it so application code must use the static Create factory, which validates inputs.
    private Account() { }

    public Guid Id { get; private set; }

    public string OwnerName { get; private set; } = default!;

    public decimal Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    // xmin is a PostgreSQL system column that PostgreSQL automatically updates with the
    // transaction ID every time a row is written.  EF Core (via Npgsql's
    // UseXminAsConcurrencyToken() configuration) reads this value when loading an account and
    // then checks it again when saving.  If another transaction changed the row in between,
    // xmin will be different and EF Core throws DbUpdateConcurrencyException — preventing a
    // "lost update" where one transaction silently overwrites another's changes.
    //
    // This property is READ-ONLY in C#.  Its value is managed entirely by PostgreSQL and is
    // populated by EF Core via a shadow property named "xmin".  We expose it here as a
    // uint so it can be logged or returned in demo responses for educational purposes.
    public uint Version { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────────────────────────


    /// Creates a new, active account.
    /// Validates inputs here (application layer) so bad data never reaches the database.
    /// The database also enforces these rules via CHECK constraints as a second safety net.

    public static Account Create(string ownerName, decimal initialBalance)
    {
        // Consistency: owner name must not be blank — enforced by both application and DB constraint.
        if (string.IsNullOrWhiteSpace(ownerName))
            throw new DomainException("Owner name cannot be empty.");

        // Consistency: opening balance must be non-negative.
        if (initialBalance < 0)
            throw new DomainException("Initial balance cannot be negative.");

        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerName = ownerName.Trim(),
            Balance = initialBalance,
            Status = AccountStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────────────────────────


    /// Increases the balance by <paramref name="amount"/>.
    /// Called by the deposit use-case, wrapped in a transaction.

    public void Deposit(decimal amount)
    {
        EnsureActive();

        // Consistency: deposit must be a positive value.
        if (amount <= 0)
            throw new DomainException("Deposit amount must be greater than zero.");

        Balance += amount;
    }


    /// Decreases the balance by <paramref name="amount"/>.
    /// The row is locked with FOR UPDATE before this is called in the service layer,
    /// preventing two concurrent withdrawals from both reading the same (pre-deduction) balance.

    public void Withdraw(decimal amount)
    {
        EnsureActive();

        if (amount <= 0)
            throw new DomainException("Withdrawal amount must be greater than zero.");

        // Consistency: balance must never go negative.
        if (Balance < amount)
            throw new InsufficientFundsException(Id, Balance, amount);

        Balance -= amount;
    }


    /// Credits this account as the receiver side of a transfer.
    /// Separated from Deposit so the ledger entry type (TransferCredit) can be distinct.

    public void Credit(decimal amount)
    {
        EnsureActive();

        if (amount <= 0)
            throw new DomainException("Credit amount must be greater than zero.");

        Balance += amount;
    }


    /// Debits this account as the sender side of a transfer.
    /// Separated from Withdraw so the ledger entry type (TransferDebit) can be distinct.

    public void Debit(decimal amount)
    {
        EnsureActive();

        if (amount <= 0)
            throw new DomainException("Debit amount must be greater than zero.");

        if (Balance < amount)
            throw new InsufficientFundsException(Id, Balance, amount);

        Balance -= amount;
    }


    /// Permanently closes the account. No further operations are possible afterwards.

    public void Close()
    {
        EnsureActive();
        Status = AccountStatus.Closed;
        ClosedAtUtc = DateTime.UtcNow;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────────────────

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
            throw new AccountNotActiveException(Id, Status);
    }
}
