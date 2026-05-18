namespace BankingLedger.Domain.Transfers;


/// The lifecycle states a transfer record can be in.
/// Consistency: only these four values are allowed by a database CHECK constraint.
/// Atomicity: a transfer starts as Pending, then either becomes Completed (all steps succeeded)
/// or RolledBack (an error occurred and the database was restored to its prior valid state).
public enum TransferStatus
{
    /// Transfer has been created but has not yet completed all steps.    
    Pending,

    /// All steps succeeded and the transaction was committed.    
    Completed,

    /// Transfer was rejected by business rules (e.g. insufficient funds) before committing.    
    Failed,

    /// An unexpected error caused the transaction to be rolled back.    
    RolledBack
}
