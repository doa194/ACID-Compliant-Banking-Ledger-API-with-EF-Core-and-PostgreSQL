using System.Data;
using BankingLedger.Application.DemoScenarios;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Ledger;
using BankingLedger.Domain.Transfers;
using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingLedger.Infrastructure.Services;

/// <summary>
/// Provides educational ACID demonstration endpoints.
/// Each method sets up a self-contained scenario and returns a structured response
/// that makes the ACID principle visible and verifiable.
/// </summary>
public sealed class DemoService
{
    private readonly BankingLedgerDbContext _db;
    private readonly ILogger<DemoService> _logger;

    public DemoService(BankingLedgerDbContext db, ILogger<DemoService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Atomicity Demo ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates Atomicity by simulating a transfer that fails partway through.
    /// The sender is debited, then an artificial exception fires before the receiver is credited.
    /// RollbackAsync() restores both balances — the database looks as if nothing happened.
    /// </summary>
    public async Task<DemoResponse> DemonstrateAtomicityAsync(CancellationToken ct = default)
    {
        var sender = Account.Create("Atomicity Demo — Sender", initialBalance: 1000m);
        var receiver = Account.Create("Atomicity Demo — Receiver", initialBalance: 500m);
        _db.Accounts.AddRange(sender, receiver);
        await _db.SaveChangesAsync(ct);

        var senderBalanceBefore = sender.Balance;
        var receiverBalanceBefore = receiver.Balance;
        long ledgerEntriesBefore = await _db.LedgerEntries.CountAsync(ct);

        // Atomicity: begin a transaction that should wrap ALL steps of a transfer.
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            sender.Debit(200m);
            await _db.SaveChangesAsync(ct);

            // Artificial failure BEFORE crediting the receiver.
            // In production this could be a network timeout, a coding bug, or a DB error.
            throw new InvalidOperationException(
                "[DEMO] Artificial failure after sender debit — before receiver credit.");
        }
        catch (InvalidOperationException)
        {
            // RollbackAsync undoes every write since BeginTransactionAsync.
            // The sender debit is reversed. Nothing happened from the database's perspective.
            await transaction.RollbackAsync(ct);
        }

        // Reload from DB to get the true persisted values (bypasses EF change tracker).
        await _db.Entry(sender).ReloadAsync(ct);
        await _db.Entry(receiver).ReloadAsync(ct);

        long ledgerEntriesAfter = await _db.LedgerEntries.CountAsync(ct);

        return new DemoResponse(
            AcidPrinciple: "Atomicity",
            Scenario: "Transfer fails after sender debit but before receiver credit",
            Result: "Transaction rolled back — both balances unchanged, no ledger entries created",
            Explanation:
                "Atomicity means all steps of a transaction succeed together or all fail together. " +
                "Even though the sender was debited inside the transaction, RollbackAsync() reversed " +
                "that debit because the transaction never committed.",
            Details: new()
            {
                ["senderBalanceBefore"] = senderBalanceBefore,
                ["senderBalanceAfter"] = sender.Balance,
                ["receiverBalanceBefore"] = receiverBalanceBefore,
                ["receiverBalanceAfter"] = receiver.Balance,
                ["ledgerEntriesCreated"] = ledgerEntriesAfter - ledgerEntriesBefore,
                ["senderBalanceUnchanged"] = sender.Balance == senderBalanceBefore,
                ["receiverBalanceUnchanged"] = receiver.Balance == receiverBalanceBefore
            }
        );
    }

    // ── Consistency Demo ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates Consistency by attempting to withdraw more than the available balance.
    /// </summary>
    public async Task<DemoResponse> DemonstrateConsistencyAsync(CancellationToken ct = default)
    {
        var account = Account.Create("Consistency Demo", initialBalance: 100m);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        var balanceBefore = account.Balance;
        string rejectedAt;
        string errorMessage;

        try
        {
            account.Withdraw(150m);  // Throws InsufficientFundsException — domain layer rejects.
            rejectedAt = "should not reach here";
            errorMessage = "none";
        }
        catch (Exception ex)
        {
            rejectedAt = "Application domain layer (InsufficientFundsException)";
            errorMessage = ex.Message;
        }

        await _db.Entry(account).ReloadAsync(ct);

        return new DemoResponse(
            AcidPrinciple: "Consistency",
            Scenario: "Withdrawal attempt exceeds available balance (150 > 100)",
            Result: "Rejected — balance remained at 100",
            Explanation:
                "Consistency means the database moves from one valid state to another valid state. " +
                "A negative balance is invalid, so both the application (InsufficientFundsException) " +
                "and the database (CHECK constraint: balance >= 0) independently block the write.",
            Details: new()
            {
                ["balanceBefore"] = balanceBefore,
                ["attemptedWithdrawal"] = 150m,
                ["balanceAfter"] = account.Balance,
                ["rejectedAt"] = rejectedAt,
                ["errorMessage"] = errorMessage,
                ["databaseConstraint"] = "CHECK (balance >= 0) — second line of defence"
            }
        );
    }

    // ── Isolation Demo ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates Isolation using pessimistic locking (FOR UPDATE).
    /// </summary>
    public async Task<DemoResponse> DemonstrateIsolationAsync(CancellationToken ct = default)
    {
        var account = Account.Create("Isolation Demo", initialBalance: 100m);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        var balanceBefore = account.Balance;
        int successCount = 0;
        int failCount = 0;

        foreach (var i in Enumerable.Range(1, 2))
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var locked = await _db.Accounts
                    .FromSqlInterpolated(
                        $"SELECT xmin, * FROM accounts WHERE id = {account.Id} FOR UPDATE")
                    .AsTracking()
                    .SingleAsync(ct);

                locked.Withdraw(70m);
                _db.LedgerEntries.Add(LedgerEntry.CreateWithdrawal(
                    locked.Id, 70m, locked.Balance, $"Isolation demo withdrawal #{i}"));

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                successCount++;

                await _db.Entry(account).ReloadAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                failCount++;
            }
        }

        await _db.Entry(account).ReloadAsync(ct);

        return new DemoResponse(
            AcidPrinciple: "Isolation",
            Scenario: "Two withdrawals of 70 on an account with balance 100",
            Result: $"{successCount} succeeded, {failCount} failed — final balance: {account.Balance}",
            Explanation:
                "SELECT ... FOR UPDATE acquires a row lock so the second withdrawal sees the " +
                "already-reduced balance (30 < 70) and throws InsufficientFundsException. " +
                "Without locking, both could read 100 and both deduct 70, leaving -40 — a corruption. " +
                "For true parallel concurrency tests see the ConcurrencyTests project.",
            Details: new()
            {
                ["balanceBefore"] = balanceBefore,
                ["balanceAfter"] = account.Balance,
                ["withdrawalAmount"] = 70m,
                ["successfulWithdrawals"] = successCount,
                ["failedWithdrawals"] = failCount,
                ["lockMechanism"] = "SELECT ... FOR UPDATE (PostgreSQL row-level exclusive lock)"
            }
        );
    }

    // ── Durability Demo ───────────────────────────────────────────────────────────────────────────

    public async Task<DemoResponse> CreateCommittedTransferAsync(CancellationToken ct = default)
    {
        var sender = Account.Create("Durability Demo — Sender", initialBalance: 500m);
        var receiver = Account.Create("Durability Demo — Receiver", initialBalance: 0m);
        _db.Accounts.AddRange(sender, receiver);
        await _db.SaveChangesAsync(ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);

        var transfer = Transfer.Create(sender.Id, receiver.Id, 250m);
        transfer.Complete();

        sender.Debit(250m);
        receiver.Credit(250m);

        _db.Transfers.Add(transfer);
        _db.LedgerEntries.Add(LedgerEntry.CreateTransferDebit(
            sender.Id, transfer.Id, 250m, sender.Balance, "Durability demo — debit"));
        _db.LedgerEntries.Add(LedgerEntry.CreateTransferCredit(
            receiver.Id, transfer.Id, 250m, receiver.Balance, "Durability demo — credit"));

        await _db.SaveChangesAsync(ct);

        // CommitAsync flushes all writes to PostgreSQL's Write-Ahead Log (WAL) on disk.
        // Durability: even if the server crashes the millisecond after this line returns,
        // PostgreSQL will replay the WAL on restart and restore the committed state.
        await transaction.CommitAsync(ct);

        return new DemoResponse(
            AcidPrinciple: "Durability",
            Scenario: "Transfer committed to PostgreSQL WAL",
            Result: $"Transfer {transfer.Id} committed — 250 moved from {sender.Id} to {receiver.Id}",
            Explanation:
                "Durability means once CommitAsync() returns, the data is on disk and survives restart. " +
                "PostgreSQL achieves this via its Write-Ahead Log (WAL). To verify: stop and restart " +
                "the PostgreSQL container, then query GET /api/transfers/{transferId} — it will still exist.",
            Details: new()
            {
                ["transferId"] = transfer.Id,
                ["senderAccountId"] = sender.Id,
                ["receiverAccountId"] = receiver.Id,
                ["amountTransferred"] = 250m,
                ["senderFinalBalance"] = sender.Balance,
                ["receiverFinalBalance"] = receiver.Balance,
                ["verificationQuery"] = $"GET /api/transfers/{transfer.Id}",
                ["manualVerification"] =
                    "docker-compose stop postgres && docker-compose start postgres, " +
                    $"then GET /api/transfers/{transfer.Id}"
            }
        );
    }
}
