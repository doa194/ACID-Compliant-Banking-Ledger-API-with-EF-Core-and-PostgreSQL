using BankingLedger.Application.Accounts;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Exceptions;
using BankingLedger.Infrastructure.Services;
using BankingLedger.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankingLedger.IntegrationTests.Accounts;

/// <summary>
/// Integration tests for account operations against a real PostgreSQL database.
/// These tests verify that ACID guarantees hold end-to-end: transactions commit,
/// balance constraints are enforced by PostgreSQL, and ledger entries are created correctly.
/// </summary>
[Collection("PostgreSQL")]
public sealed class AccountIntegrationTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AccountIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private AccountService CreateService() =>
        new(_fixture.CreateDbContext(), NullLogger<AccountService>.Instance);

    [Fact]
    public async Task CreateAccount_WithInitialBalance_PersistsAndCreatesLedgerEntry()
    {
        // Atomicity + Durability: the account AND the initial deposit ledger entry are
        // both committed in one transaction and visible to the next query.
        var service = CreateService();
        var result = await service.CreateAccountAsync(
            new CreateAccountRequest("Integration Test User", 500m));

        result.Id.Should().NotBe(Guid.Empty);
        result.Balance.Should().Be(500m);
        result.Status.Should().Be("Active");

        // Verify the ledger entry was also persisted.
        var ledger = await service.GetLedgerAsync(result.Id);
        ledger.Should().HaveCount(1);
        ledger[0].Type.Should().Be("Deposit");
        ledger[0].Amount.Should().Be(500m);
        ledger[0].BalanceAfterTransaction.Should().Be(500m);
    }

    [Fact]
    public async Task Deposit_IncreasesBalanceAndCreatesLedgerEntry()
    {
        var service = CreateService();
        var account = await service.CreateAccountAsync(new CreateAccountRequest("DepositUser", 100m));

        var result = await service.DepositAsync(account.Id, new DepositRequest(250m, "Test deposit"));

        result.Balance.Should().Be(350m);

        var ledger = await service.GetLedgerAsync(account.Id);
        // Two entries: initial deposit + this deposit.
        ledger.Should().HaveCount(2);
        ledger.Last().Type.Should().Be("Deposit");
        ledger.Last().Amount.Should().Be(250m);
        ledger.Last().BalanceAfterTransaction.Should().Be(350m);
    }

    [Fact]
    public async Task Withdraw_WithSufficientFunds_DecreasesBalanceAndCreatesLedgerEntry()
    {
        var service = CreateService();
        var account = await service.CreateAccountAsync(new CreateAccountRequest("WithdrawUser", 200m));

        var result = await service.WithdrawAsync(account.Id, new WithdrawRequest(75m, "Test withdrawal"));

        result.Balance.Should().Be(125m);

        var ledger = await service.GetLedgerAsync(account.Id);
        ledger.Last().Type.Should().Be("Withdrawal");
        ledger.Last().Amount.Should().Be(75m);
        ledger.Last().BalanceAfterTransaction.Should().Be(125m);
    }

    [Fact]
    public async Task Withdraw_InsufficientFunds_ThrowsAndBalanceUnchanged()
    {
        // Consistency: the database balance must never go negative.
        var service = CreateService();
        var account = await service.CreateAccountAsync(new CreateAccountRequest("PoorUser", 50m));

        var act = async () =>
            await service.WithdrawAsync(account.Id, new WithdrawRequest(100m, "Overdraft attempt"));

        await act.Should().ThrowAsync<InsufficientFundsException>();

        // Verify the balance is still 50 — nothing was persisted.
        var balance = await service.GetBalanceAsync(account.Id);
        balance.Should().Be(50m);
    }

    [Fact]
    public async Task CloseAccount_PreventsSubsequentDeposits()
    {
        // Consistency: closed accounts must not accept further modifications.
        var service = CreateService();
        var account = await service.CreateAccountAsync(new CreateAccountRequest("ClosingUser", 0m));
        await service.CloseAccountAsync(account.Id);

        var act = async () =>
            await service.DepositAsync(account.Id, new DepositRequest(100m, "Deposit to closed account"));

        await act.Should().ThrowAsync<AccountNotActiveException>();
    }

    [Fact]
    public async Task GetNonExistentAccount_ThrowsAccountNotFoundException()
    {
        var service = CreateService();
        var act = async () => await service.GetAccountAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<AccountNotFoundException>();
    }
}
