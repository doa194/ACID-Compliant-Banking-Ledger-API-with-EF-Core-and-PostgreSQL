using System.Data;
using BankingLedger.ConcurrencyTests.Infrastructure;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Ledger;
using BankingLedger.Domain.Transfers;
using BankingLedger.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BankingLedger.ConcurrencyTests;

/// <summary>
/// Test 2 from the spec: Concurrent Transfers.
///
/// Given: Account A balance = 100, Account B balance = 0
/// When:  5 parallel transfers of 50 from A to B
/// Expected:
///   - Only 2 transfers succeed (2 × 50 = 100)
///   - Account A balance = 0
///   - Account B balance = 100
///   - Ledger has 4 transfer entries (2 successful × 2 entries each)
///
/// This test proves both ATOMICITY and ISOLATION via ordered pessimistic locking.
/// It also demonstrates deadlock prevention: by always locking the account with the
/// smaller GUID first, concurrent transfers that involve the same two accounts always
/// acquire locks in the same order and therefore queue instead of deadlocking.
/// </summary>
[Collection("ConcurrencyTests")]
public sealed class ConcurrentTransferTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;

    public ConcurrentTransferTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FiveConcurrentTransfers_OnlyTwoSucceed_BothBalancesCorrect()
    {
        // ── Setup ─────────────────────────────────────────────────────────────────────────────────
        await using var setupDb = _fixture.CreateDbContext();
        var accountA = Account.Create("Concurrent Transfer A", initialBalance: 100m);
        var accountB = Account.Create("Concurrent Transfer B", initialBalance: 0m);
        setupDb.Accounts.AddRange(accountA, accountB);
        await setupDb.SaveChangesAsync();

        var idA = accountA.Id;
        var idB = accountB.Id;

        // ── Act: 5 concurrent transfers of 50 from A to B ─────────────────────────────────────
        var tasks = Enumerable.Range(1, 5).Select(_ =>
            TransferWithOrderedLockAsync(idA, idB, amount: 50m));

        var results = await Task.WhenAll(tasks);

        // ── Assert ────────────────────────────────────────────────────────────────────────────────
        int succeeded = results.Count(r => r);
        int failed = results.Count(r => !r);

        succeeded.Should().Be(2,
            because: "Account A has 100 and each transfer is 50 — only 2 fit");
        failed.Should().Be(3,
            because: "3 transfers see insufficient funds after the first 2 committed");

        await using var verifyDb = _fixture.CreateDbContext();
        var finalA = await verifyDb.Accounts.FindAsync(idA);
        var finalB = await verifyDb.Accounts.FindAsync(idB);

        finalA!.Balance.Should().Be(0m,
            because: "A transferred all 100 in two successful transfers of 50");
        finalB!.Balance.Should().Be(100m,
            because: "B received 50 twice = 100");

        // 2 successful transfers × 2 ledger entries each = 4 total transfer ledger entries.
        var transferLedgerEntries = await verifyDb.LedgerEntries
            .Where(l => (l.AccountId == idA || l.AccountId == idB)
                     && (l.Type == LedgerEntryType.TransferDebit || l.Type == LedgerEntryType.TransferCredit))
            .ToListAsync();

        transferLedgerEntries.Should().HaveCount(4,
            because: "each successful transfer creates 1 TransferDebit + 1 TransferCredit = 2 entries");
        transferLedgerEntries.Count(e => e.Type == LedgerEntryType.TransferDebit).Should().Be(2);
        transferLedgerEntries.Count(e => e.Type == LedgerEntryType.TransferCredit).Should().Be(2);
    }

    /// <summary>
    /// Executes a transfer using the ordered-lock strategy from TransferService.
    /// Returns true on commit, false on failure (insufficient funds, etc.).
    /// </summary>
    private async Task<bool> TransferWithOrderedLockAsync(Guid fromId, Guid toId, decimal amount)
    {
        await using var db = _fixture.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            // ISOLATION — Deadlock prevention via consistent lock ordering:
            // Always lock the account with the lexicographically smaller GUID first.
            // If multiple transfers involve accounts A and B, they all acquire locks in the
            // same order (smaller ID first), so they QUEUE instead of deadlocking.
            // A deadlock would occur if Transfer 1 locks A then waits for B, while
            // Transfer 2 locked B first and is waiting for A — circular wait = deadlock.
            var (firstId, secondId) = fromId.CompareTo(toId) < 0
                ? (fromId, toId)
                : (toId, fromId);

            var firstAccount = await db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {firstId} FOR UPDATE")
                .AsTracking()
                .SingleAsync();

            var secondAccount = await db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {secondId} FOR UPDATE")
                .AsTracking()
                .SingleAsync();

            var fromAccount = firstId == fromId ? firstAccount : secondAccount;
            var toAccount = firstId == toId ? firstAccount : secondAccount;

            // Domain validation — throws InsufficientFundsException if balance < amount.
            fromAccount.Debit(amount);
            toAccount.Credit(amount);

            var transfer = Transfer.Create(fromId, toId, amount);
            transfer.Complete();
            db.Transfers.Add(transfer);

            // Double-entry ledger — one debit entry + one credit entry per transfer.
            db.LedgerEntries.Add(LedgerEntry.CreateTransferDebit(
                fromId, transfer.Id, amount, fromAccount.Balance, "Concurrent transfer test — debit"));
            db.LedgerEntries.Add(LedgerEntry.CreateTransferCredit(
                toId, transfer.Id, amount, toAccount.Balance, "Concurrent transfer test — credit"));

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
