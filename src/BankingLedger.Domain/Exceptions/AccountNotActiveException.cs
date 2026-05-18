using BankingLedger.Domain.Accounts;

namespace BankingLedger.Domain.Exceptions;

/// Thrown when an operation is attempted on an account that is not in Active status.
/// Consistency: only Active accounts may be credited, debited, or transferred from/to.
/// Closed or Frozen accounts are immutable from a business perspective.
public sealed class AccountNotActiveException : DomainException
{
    public Guid AccountId { get; }
    public AccountStatus CurrentStatus { get; }

    public AccountNotActiveException(Guid accountId, AccountStatus currentStatus)
        : base($"Account {accountId} is not active (current status: {currentStatus}). Only Active accounts can be used for transactions.")
    {
        AccountId = accountId;
        CurrentStatus = currentStatus;
    }
}
