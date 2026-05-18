namespace BankingLedger.Application.Transfers;

/// Read model returned for transfer queries.
public record TransferResponse(
    Guid Id,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Status,
    DateTime CreatedAtUtc,
    string? FailureReason
);
