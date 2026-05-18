namespace BankingLedger.Domain.Exceptions;

/// Thrown when a transfer record cannot be found by ID.
public sealed class TransferNotFoundException : DomainException
{
    public Guid TransferId { get; }

    public TransferNotFoundException(Guid transferId)
        : base($"Transfer with ID '{transferId}' was not found.")
    {
        TransferId = transferId;
    }
}
