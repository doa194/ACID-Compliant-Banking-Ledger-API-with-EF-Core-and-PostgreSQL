namespace BankingLedger.Domain.Exceptions;

/// Thrown when a debit or withdrawal would push an account balance below zero.
/// Consistency: the balance >= 0 invariant must always hold. This exception prevents
/// the application from even attempting a database write that would violate the constraint.
/// The database also has a CHECK constraint (balance >= 0) as a second layer of protection.
public sealed class InsufficientFundsException : DomainException
{
    public Guid AccountId { get; }
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }

    public InsufficientFundsException(Guid accountId, decimal currentBalance, decimal requestedAmount)
        : base($"Account {accountId} has insufficient funds. Balance: {currentBalance:F2}, requested: {requestedAmount:F2}.")
    {
        AccountId = accountId;
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
    }
}
