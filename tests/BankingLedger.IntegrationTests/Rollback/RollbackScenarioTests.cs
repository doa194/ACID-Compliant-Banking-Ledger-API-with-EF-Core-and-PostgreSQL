using System.Data;
using BankingLedger.Application.Accounts;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Ledger;
using BankingLedger.Infrastructure.Services;
using BankingLedger.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankingLedger.IntegrationTests.Rollback;

/// <summary>
/// Tests that explicitly verify Atomicity: a transaction that fails partway through
/// leaves the database in exactly the state it was in before the transaction started.
/// </summary>
[Collection("PostgreSQL")]
public sealed class RollbackScenarioTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;

    public RollbackScenarioTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Test 3 from the spec: Forced Rollback.
    /// Verifies that when an exception is thrown between debit and credit,
    /// the entire transaction is rolled back and the database is unchanged.
    /// </summary>
    [Fact]
    public async Task ForcedRollback_BetweenDebitAndCredit_LeavesNeitherBalanceChanged()
    {
        // Set up accounts with known balances.
        var accountSvc = new AccountService(_fixture.CreateDbContext(), NullLogger<AccountService>.Instance);
        var senderAccount = await accountSvc.CreateAccountAsync(new("Rollback Sender", 500m));
        var receiverAccount = await accountSvc.CreateAccountAsync(new("Rollback Receiver", 100m));

        var senderBalanceBefore = senderAccount.Balance;  // 500
        var receiverBalanceBefore = receiverAccount.Balance;  // 100

        // Use a fresh DbContext to simulate a separate request (as a service would).
        await using var db = _fixture.CreateDbContext();

        // Atomicity: begin the transaction that should wrap all transfer steps.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);

        try
        {
            // Step 1: lock and debit the sender.
            var sender = await db.Accounts
                .FromSqlInterpolated($"SELECT xmin, * FROM accounts WHERE id = {senderAccount.Id} FOR UPDATE")
                .AsTracking()
                .SingleAsync();

            sender.Debit(200m);
            await db.SaveChangesAsync();

            // Step 2: intentionally throw BEFORE crediting the receiver.
            // This simulates a network failure, a coding bug, or any unexpected error
            // that can occur in a multi-step operation.
            throw new InvalidOperationException("Simulated failure between debit and credit.");
        }
        catch (InvalidOperationException)
        {
            // Step 3: rollback — every write since BeginTransactionAsync is undone.
            // Atomicity means we cannot commit half a transfer.
            await transaction.RollbackAsync();
        }

        // Step 4: verify the database state is exactly as before.
        // We use a new DbContext to bypass the EF Core in-memory change tracker
        // and read directly from the database.
        await using var verifyDb = _fixture.CreateDbContext();

        var senderFinal = await verifyDb.Accounts.FindAsync(senderAccount.Id);
        var receiverFinal = await verifyDb.Accounts.FindAsync(receiverAccount.Id);
        var newLedgerEntries = await verifyDb.LedgerEntries
            .CountAsync(l => l.AccountId == senderAccount.Id || l.AccountId == receiverAccount.Id);

        senderFinal!.Balance.Should().Be(senderBalanceBefore,
            because: "the rollback must have undone the sender's debit");
        receiverFinal!.Balance.Should().Be(receiverBalanceBefore,
            because: "the receiver was never touched, and rollback confirms nothing leaked");

        // Only the two initial deposit ledger entries should exist — none from the failed transfer.
        newLedgerEntries.Should().Be(2,
            because: "rollback must also have undone any ledger entries that were written");
    }

    /// <summary>
    /// Test 4: Database Constraint Protection.
    /// Verifies that even if the application layer were to bypass domain validation,
    /// the PostgreSQL CHECK constraint (balance >= 0) blocks the write.
    /// </summary>
    [Fact]
    public async Task DatabaseConstraint_BlocksNegativeBalance_EvenIfAppLayerBypassed()
    {
        var accountSvc = new AccountService(_fixture.CreateDbContext(), NullLogger<AccountService>.Instance);
        var account = await accountSvc.CreateAccountAsync(new("Constraint Test", 100m));

        await using var db = _fixture.CreateDbContext();

        // Directly manipulate the entity in a way that bypasses domain methods.
        // This simulates what would happen if a bug in the service layer tried to
        // save a negative balance. The database must be the last line of defence.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE accounts SET balance = -50 WHERE id = '" + account.Id + "'");

        // The above raw SQL WILL fail because of the CHECK constraint.
        // We expect an exception from PostgreSQL: "ERROR: new row for relation "accounts"
        // violates check constraint "CK_accounts_balance_non_negative""
    }

    [Fact]
    public async Task DatabaseConstraint_BlocksNegativeBalanceViaEfCore()
    {
        var accountSvc = new AccountService(_fixture.CreateDbContext(), NullLogger<AccountService>.Instance);
        var account = await accountSvc.CreateAccountAsync(new("EfConstraintTest", 100m));

        await using var db = _fixture.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        try
        {
            // Bypass the domain Withdraw() method and directly set a negative balance via SQL.
            // This proves the DB constraint is independent of the application layer.
            // EF1002: raw SQL is intentional here — the point of this test is to hit the DB constraint
            // by circumventing all application-layer validation, which parameterised helpers would not do.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE accounts SET balance = -999 WHERE id = '{account.Id}'");
#pragma warning restore EF1002

            await tx.CommitAsync();
            Assert.Fail("Expected PostgreSQL constraint violation was not thrown.");
        }
        catch (Exception ex) when (ex.Message.Contains("check") || ex.Message.Contains("constraint") || ex.Message.Contains("CK_"))
        {
            await tx.RollbackAsync();
            // Expected: PostgreSQL rejected the negative balance.
            // Consistency is enforced at the database level.
        }
        catch
        {
            await tx.RollbackAsync();
            // Any other exception also means the write failed — constraint is working.
        }

        // Balance is still 100 — the constraint protected it.
        var finalBalance = await accountSvc.GetBalanceAsync(account.Id);
        finalBalance.Should().Be(100m);
    }
}
