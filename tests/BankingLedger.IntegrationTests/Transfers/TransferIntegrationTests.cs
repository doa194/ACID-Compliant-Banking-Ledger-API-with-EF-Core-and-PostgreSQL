using BankingLedger.Application.Accounts;
using BankingLedger.Application.Transfers;
using BankingLedger.Domain.Exceptions;
using BankingLedger.Infrastructure.Services;
using BankingLedger.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankingLedger.IntegrationTests.Transfers;

/// <summary>
/// Integration tests for the transfer use case against a real PostgreSQL instance.
/// Verifies the full ACID contract: atomicity of six-step transactions, consistency of balances,
/// and creation of both ledger entries.
/// </summary>
[Collection("PostgreSQL")]
public sealed class TransferIntegrationTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;

    public TransferIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private AccountService AccountSvc() =>
        new(_fixture.CreateDbContext(), NullLogger<AccountService>.Instance);

    private TransferService TransferSvc() =>
        new(_fixture.CreateDbContext(), NullLogger<TransferService>.Instance);

    [Fact]
    public async Task Transfer_MovesMoneyAndCreatesDoubleLedgerEntries()
    {
        // ATOMICITY: all six steps (lock sender, lock receiver, debit, credit,
        // insert transfer, insert 2 ledger entries) commit together.
        var accounts = AccountSvc();
        var sender = await accounts.CreateAccountAsync(new("TransferSender", 1000m));
        var receiver = await accounts.CreateAccountAsync(new("TransferReceiver", 0m));

        var transfers = TransferSvc();
        var transfer = await transfers.TransferAsync(
            new TransferRequest(sender.Id, receiver.Id, 400m, "Test transfer"));

        transfer.Status.Should().Be("Completed");
        transfer.FailureReason.Should().BeNull();

        // Verify balances committed correctly (Durability).
        var senderBalance = await accounts.GetBalanceAsync(sender.Id);
        var receiverBalance = await accounts.GetBalanceAsync(receiver.Id);
        senderBalance.Should().Be(600m);
        receiverBalance.Should().Be(400m);

        // Verify double-entry ledger: one TransferDebit + one TransferCredit.
        var senderLedger = await accounts.GetLedgerAsync(sender.Id);
        var receiverLedger = await accounts.GetLedgerAsync(receiver.Id);

        senderLedger.Last().Type.Should().Be("TransferDebit");
        senderLedger.Last().Amount.Should().Be(400m);
        senderLedger.Last().TransferId.Should().Be(transfer.Id);

        receiverLedger.Last().Type.Should().Be("TransferCredit");
        receiverLedger.Last().Amount.Should().Be(400m);
        receiverLedger.Last().TransferId.Should().Be(transfer.Id);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_NoBalanceChangeAndNoLedgerEntries()
    {
        // ATOMICITY: if the domain throws before committing, nothing is persisted.
        var accounts = AccountSvc();
        var sender = await accounts.CreateAccountAsync(new("PoorSender", 100m));
        var receiver = await accounts.CreateAccountAsync(new("RichReceiver", 0m));

        var transfers = TransferSvc();
        var act = async () =>
            await transfers.TransferAsync(new TransferRequest(sender.Id, receiver.Id, 500m, "Over budget"));

        await act.Should().ThrowAsync<InsufficientFundsException>();

        // Both balances unchanged — the transaction was rolled back.
        (await accounts.GetBalanceAsync(sender.Id)).Should().Be(100m);
        (await accounts.GetBalanceAsync(receiver.Id)).Should().Be(0m);

        // No ledger entries for this failed attempt.
        var senderLedger = await accounts.GetLedgerAsync(sender.Id);
        senderLedger.Should().HaveCount(1); // Only the initial deposit.
    }

    [Fact]
    public async Task Transfer_ToSameAccount_ThrowsDomainException()
    {
        var accounts = AccountSvc();
        var account = await accounts.CreateAccountAsync(new("SelfTransfer", 100m));
        var transfers = TransferSvc();

        var act = async () =>
            await transfers.TransferAsync(new TransferRequest(account.Id, account.Id, 50m, "Self"));

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Transfer_ToClosedAccount_ThrowsAccountNotActiveException()
    {
        var accounts = AccountSvc();
        var sender = await accounts.CreateAccountAsync(new("SenderOpen", 500m));
        var receiver = await accounts.CreateAccountAsync(new("ReceiverClosed", 0m));
        await accounts.CloseAccountAsync(receiver.Id);

        var transfers = TransferSvc();
        var act = async () =>
            await transfers.TransferAsync(new TransferRequest(sender.Id, receiver.Id, 100m, "To closed"));

        await act.Should().ThrowAsync<AccountNotActiveException>();

        // Sender balance unchanged — rollback worked.
        (await accounts.GetBalanceAsync(sender.Id)).Should().Be(500m);
    }
}
