namespace BankingLedger.Application.Accounts;

/// Read model returned for account queries.
public record AccountResponse(
    Guid Id,
    string OwnerName,
    decimal Balance,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc
);

/// Read model returned for a single ledger entry.
public record LedgerEntryResponse(
    Guid Id,
    Guid AccountId,
    Guid? TransferId,
    string Type,
    decimal Amount,
    decimal BalanceAfterTransaction,
    string Description,
    DateTime CreatedAtUtc
);
