namespace BankingLedger.Domain.Exceptions;

/// Base exception for all business-rule violations in the domain layer.
/// These exceptions represent Consistency failures — operations that would move the system
/// from a valid state to an invalid one are rejected before reaching the database.
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}
