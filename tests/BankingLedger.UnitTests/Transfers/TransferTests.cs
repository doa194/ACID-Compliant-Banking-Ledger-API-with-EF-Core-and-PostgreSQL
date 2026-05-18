using BankingLedger.Domain.Transfers;
using FluentAssertions;

namespace BankingLedger.UnitTests.Transfers;

/// <summary>
/// Unit tests for Transfer domain entity state transitions.
/// </summary>
public sealed class TransferTests
{
    [Fact]
    public void Create_NewTransfer_IsInPendingState()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var transfer = Transfer.Create(from, to, 100m);

        transfer.Id.Should().NotBe(Guid.Empty);
        transfer.FromAccountId.Should().Be(from);
        transfer.ToAccountId.Should().Be(to);
        transfer.Amount.Should().Be(100m);
        // Atomicity: a transfer starts as Pending — it transitions to Completed only when
        // ALL steps succeed and CommitAsync() is called.
        transfer.Status.Should().Be(TransferStatus.Pending);
        transfer.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Complete_PendingTransfer_SetsCompletedStatus()
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 50m);
        transfer.Complete();
        transfer.Status.Should().Be(TransferStatus.Completed);
    }

    [Fact]
    public void Fail_PendingTransfer_SetsFailedStatusAndReason()
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 50m);
        transfer.Fail("Insufficient funds");
        transfer.Status.Should().Be(TransferStatus.Failed);
        transfer.FailureReason.Should().Be("Insufficient funds");
    }

    [Fact]
    public void MarkRolledBack_SetsRolledBackStatusAndReason()
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 50m);
        transfer.MarkRolledBack("Unexpected exception");
        transfer.Status.Should().Be(TransferStatus.RolledBack);
        transfer.FailureReason.Should().Be("Unexpected exception");
    }

    [Fact]
    public void Create_RecordsCorrectTimestamp()
    {
        var before = DateTime.UtcNow;
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 1m);
        var after = DateTime.UtcNow;
        transfer.CreatedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
