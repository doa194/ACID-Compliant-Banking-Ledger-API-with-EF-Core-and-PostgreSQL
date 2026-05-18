using System.Data;
using BankingLedger.Application.Transfers;
using BankingLedger.Domain.Exceptions;
using BankingLedger.Domain.Ledger;
using BankingLedger.Domain.Transfers;
using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingLedger.Infrastructure.Services;

/// <summary>
/// Executes money transfers between accounts.
/// Lives in Infrastructure because it owns the full ACID transaction logic with EF Core.
/// </summary>
public sealed class TransferService
{
    private readonly BankingLedgerDbContext _db;
    private readonly ILogger<TransferService> _logger;

    public TransferService(BankingLedgerDbContext db, ILogger<TransferService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Queries ───────────────────────────────────────────────────────────────────────────────────

    public async Task<TransferResponse> GetTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        var transfer = await _db.Transfers.FindAsync(new object[] { transferId }, ct)
            ?? throw new TransferNotFoundException(transferId);
        return MapToResponse(transfer);
    }

    public async Task<IReadOnlyList<TransferResponse>> GetTransfersAsync(CancellationToken ct = default)
    {
        return await _db.Transfers
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TransferResponse(
                t.Id, t.FromAccountId, t.ToAccountId,
                t.Amount, t.Status.ToString(), t.CreatedAtUtc, t.FailureReason))
            .ToListAsync(ct);
    }

    // ── Command ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transfers money from one account to another.
    ///
    /// Full ACID breakdown:
    ///
    /// ATOMICITY — all six steps are inside one transaction:
    ///   1. Lock sender row (FOR UPDATE)
    ///   2. Lock receiver row (FOR UPDATE)
    ///   3. Debit sender balance
    ///   4. Credit receiver balance
    ///   5. Insert Transfer record
    ///   6. Insert two LedgerEntry rows
    ///   If any step throws, RollbackAsync() undoes all of them.
    ///
    /// CONSISTENCY — domain rules prevent invalid states:
    ///   Both accounts must be Active; sender must have sufficient funds.
    ///
    /// ISOLATION — ordered pessimistic locking prevents deadlocks:
    ///   Always lock the account with the smaller GUID first.
    ///   This ensures all concurrent transfers on the same pair queue, not deadlock.
    ///
    /// DURABILITY — CommitAsync() flushes to the WAL.
    ///   A Completed transfer survives a server crash after commit.
    /// </summary>
    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken ct = default)
    {
        if (request.FromAccountId == request.ToAccountId)
            throw new DomainException("Source and destination accounts must be different.");

        // Atomicity: begin a single transaction wrapping all six steps.
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            // ── ISOLATION: Ordered Pessimistic Locking ────────────────────────────────────────────
            // Lock the account with the smaller GUID first.
            // All concurrent transfers involving the same pair always lock in the same order,
            // so they queue rather than deadlock.
            var (firstId, secondId) = request.FromAccountId.CompareTo(request.ToAccountId) < 0
                ? (request.FromAccountId, request.ToAccountId)
                : (request.ToAccountId, request.FromAccountId);

            var firstAccount = await _db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {firstId} FOR UPDATE")
                .AsTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new AccountNotFoundException(firstId);

            var secondAccount = await _db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {secondId} FOR UPDATE")
                .AsTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new AccountNotFoundException(secondId);

            var fromAccount = firstId == request.FromAccountId ? firstAccount : secondAccount;
            var toAccount = firstId == request.ToAccountId ? firstAccount : secondAccount;

            // ── CONSISTENCY: Domain validation ────────────────────────────────────────────────────
            fromAccount.Debit(request.Amount);
            toAccount.Credit(request.Amount);

            // ── Create the Transfer record ────────────────────────────────────────────────────────
            var transfer = Transfer.Create(request.FromAccountId, request.ToAccountId, request.Amount);
            transfer.Complete();
            _db.Transfers.Add(transfer);

            // ── Double-entry ledger ───────────────────────────────────────────────────────────────
            // A transfer always produces exactly two ledger entries:
            // - TransferDebit: money left the sender's account
            // - TransferCredit: money arrived in the receiver's account
            _db.LedgerEntries.Add(LedgerEntry.CreateTransferDebit(
                request.FromAccountId, transfer.Id, request.Amount, fromAccount.Balance,
                $"Transfer to {request.ToAccountId}: {request.Description}"));

            _db.LedgerEntries.Add(LedgerEntry.CreateTransferCredit(
                request.ToAccountId, transfer.Id, request.Amount, toAccount.Balance,
                $"Transfer from {request.FromAccountId}: {request.Description}"));

            await _db.SaveChangesAsync(ct);

            // Durability: all six writes become persistent simultaneously.
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Transfer {TransferId}: {Amount} from {From} to {To}",
                transfer.Id, request.Amount, request.FromAccountId, request.ToAccountId);

            return MapToResponse(transfer);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "Transfer from {From} to {To} failed and was rolled back",
                request.FromAccountId, request.ToAccountId);
            throw;
        }
    }

    public static TransferResponse MapToResponse(Transfer t) =>
        new(t.Id, t.FromAccountId, t.ToAccountId,
            t.Amount, t.Status.ToString(), t.CreatedAtUtc, t.FailureReason);
}
