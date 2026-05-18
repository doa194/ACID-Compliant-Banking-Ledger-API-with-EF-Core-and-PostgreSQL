using System.Data;
using BankingLedger.Application.Accounts;
using BankingLedger.ConcurrencyTests.Infrastructure;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Ledger;
using BankingLedger.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankingLedger.ConcurrencyTests;

/// <summary>
/// Test 1 from the spec: Concurrent Withdrawals.
///
/// Given: balance = 100
/// When:  10 parallel requests each try to withdraw 30
/// Expected:
///   - Only 3 withdrawals succeed (3 × 30 = 90 ≤ 100)
///   - Final balance = 10
///   - No negative balance at any point
///   - Ledger entries exactly match successful withdrawals
///
/// This test proves ISOLATION via pessimistic locking (FOR UPDATE):
/// Without the row lock, multiple threads could all read 100 and all decide 100 >= 30.
/// All would then deduct 30, leaving a balance of -200 — a severe corruption.
/// With FOR UPDATE, each withdrawal acquires an exclusive lock, waits for the previous to
/// commit, then reads the true (already-reduced) balance before deciding.
/// </summary>
[Collection("ConcurrencyTests")]
public sealed class ConcurrentWithdrawalTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;

    public ConcurrentWithdrawalTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TenConcurrentWithdrawals_OnlyThreeSucceed_BalanceNeverNegative()
    {
        // ── Setup ─────────────────────────────────────────────────────────────────────────────────
        await using var setupDb = _fixture.CreateDbContext();
        var account = Account.Create("Concurrent Withdrawal Account", initialBalance: 100m);
        setupDb.Accounts.Add(account);
        await setupDb.SaveChangesAsync();
        var accountId = account.Id;

        // ── Act: 10 concurrent withdrawal attempts ─────────────────────────────────────────────
        var tasks = Enumerable.Range(1, 10).Select(_ =>
            WithdrawWithPessimisticLockAsync(accountId, amount: 30m));

        var results = await Task.WhenAll(tasks);

        // ── Assert ────────────────────────────────────────────────────────────────────────────────
        int succeeded = results.Count(r => r);
        int failed = results.Count(r => !r);

        succeeded.Should().Be(3,
            because: "100 / 30 = 3 full withdrawals fit in the balance (remainder 10)");
        failed.Should().Be(7,
            because: "7 requests should see insufficient funds after the first 3 succeed");

        // Reload from DB to get the committed balance — bypass the change tracker.
        await using var verifyDb = _fixture.CreateDbContext();
        var final = await verifyDb.Accounts.FindAsync(accountId);
        final!.Balance.Should().Be(10m,
            because: "100 - (3 × 30) = 10");
        final.Balance.Should().BeGreaterThanOrEqualTo(0m,
            because: "balance must NEVER be negative — this is the Consistency guarantee");

        // Verify ledger entries exactly match successful withdrawals.
        var entries = await verifyDb.LedgerEntries
            .Where(l => l.AccountId == accountId && l.Type == LedgerEntryType.Withdrawal)
            .ToListAsync();
        entries.Should().HaveCount(3,
            because: "one ledger entry per successful withdrawal");
        entries.All(e => e.Amount == 30m).Should().BeTrue();
    }

    /// <summary>
    /// Attempts a single withdrawal using pessimistic locking.
    /// Returns true if it committed, false if insufficient funds.
    /// </summary>
    private async Task<bool> WithdrawWithPessimisticLockAsync(Guid accountId, decimal amount)
    {
        await using var db = _fixture.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            // FOR UPDATE: locks this row so other concurrent transactions must wait.
            // Each withdrawal sees the balance AFTER all previous ones committed.
            var account = await db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {accountId} FOR UPDATE")
                .AsTracking()
                .SingleAsync();

            account.Withdraw(amount);  // Throws InsufficientFundsException if balance < amount.

            db.LedgerEntries.Add(LedgerEntry.CreateWithdrawal(
                accountId, amount, account.Balance, "Concurrent withdrawal test"));

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            return false;
        }
    }
}
