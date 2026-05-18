namespace BankingLedger.Application.Transfers;

/// Input for initiating a money transfer between two accounts.
public record TransferRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Description
);
