namespace BankingLedger.Domain.Exceptions;

/// Thrown when an account lookup by ID returns no result.
/// Used in service methods that require the account to exist before proceeding.
public sealed class AccountNotFoundException : DomainException
{
    public Guid AccountId { get; }

    public AccountNotFoundException(Guid accountId)
        : base($"Account with ID '{accountId}' was not found.")
    {
        AccountId = accountId;
    }
}
