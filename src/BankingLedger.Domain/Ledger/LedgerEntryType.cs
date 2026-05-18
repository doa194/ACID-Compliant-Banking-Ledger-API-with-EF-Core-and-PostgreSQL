namespace BankingLedger.Domain.Ledger;


/// Classifies every ledger entry so the audit trail is self-describing.
/// Consistency: the database enforces a CHECK constraint that only these four values are stored.
/// A transfer always produces exactly two entries: one TransferDebit (sender) and one
/// TransferCredit (receiver). This double-entry pattern is how real banking ledgers work.

public enum LedgerEntryType
{
    /// Money was deposited into the account.
    Deposit,

    /// Money was withdrawn from the account.
    Withdrawal,

    /// Money left this account as part of a transfer (sender side).
    TransferDebit,

    /// Money arrived into this account as part of a transfer (receiver side).
    TransferCredit
}
