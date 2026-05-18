namespace BankingLedger.Domain.Accounts;


/// The lifecycle states an account can be in.
/// Consistency: the database enforces a CHECK constraint that only these three values are valid.
/// Application code also validates status before any operation, so the account can never be
/// used in an invalid state from either the database or the application side.

public enum AccountStatus
{
    ///Account is fully operational — deposits, withdrawals and transfers are allowed.
    Active,

    /// Account has been permanently closed — no further modifications are permitted.
    Closed,

    ///Account is temporarily suspended — reserved for future use (e.g. fraud holds).
    Frozen
}
