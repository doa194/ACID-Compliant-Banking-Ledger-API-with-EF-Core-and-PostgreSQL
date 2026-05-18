using System.Data;
using BankingLedger.Application.Accounts;
using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Exceptions;
using BankingLedger.Domain.Ledger;
using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankingLedger.Infrastructure.Services;


/// Orchestrates all account-related use cases.
/// Lives in Infrastructure because it directly uses BankingLedgerDbContext and EF Core.
/// The Application layer provides the DTOs and validators; this layer provides the
/// transaction-aware execution.
public sealed class AccountService
{
    private readonly BankingLedgerDbContext _db;
    private readonly ILogger<AccountService> _logger;

    public AccountService(BankingLedgerDbContext db, ILogger<AccountService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Queries ───────────────────────────────────────────────────────────────────────────────────

    public async Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await FindOrThrowAsync(accountId, ct);
        return MapToResponse(account);
    }

    public async Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await FindOrThrowAsync(accountId, ct);
        return account.Balance;
    }

    public async Task<IReadOnlyList<LedgerEntryResponse>> GetLedgerAsync(Guid accountId, CancellationToken ct = default)
    {
        await FindOrThrowAsync(accountId, ct);
        return await _db.LedgerEntries
            .Where(l => l.AccountId == accountId)
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new LedgerEntryResponse(
                l.Id, l.AccountId, l.TransferId,
                l.Type.ToString(), l.Amount, l.BalanceAfterTransaction,
                l.Description, l.CreatedAtUtc))
            .ToListAsync(ct);
    }

    // ── Commands ──────────────────────────────────────────────────────────────────────────────────

    
    /// Creates a new account and, if an initial balance is provided, records a deposit ledger entry.
    /// Atomicity: both writes are inside one transaction.

    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        var account = Account.Create(request.OwnerName, request.InitialBalance);

        // Atomicity: begin a transaction that wraps both the account INSERT and the
        // optional ledger entry INSERT.  If anything fails, both are rolled back.
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            _db.Accounts.Add(account);

            if (request.InitialBalance > 0)
            {
                var entry = LedgerEntry.CreateDeposit(
                    account.Id, request.InitialBalance,
                    balanceAfter: request.InitialBalance,
                    description: "Initial deposit on account opening");
                _db.LedgerEntries.Add(entry);
            }

            await _db.SaveChangesAsync(ct);

            // CommitAsync makes all writes durable and visible to other transactions.
            // Durability: once this returns, the account survives a server restart.
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Account {AccountId} created for {OwnerName}", account.Id, account.OwnerName);
            return MapToResponse(account);
        }
        catch
        {
            // RollbackAsync undoes every write since BeginTransactionAsync.
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    
    /// Deposits money into an account.
    /// Atomicity: the balance UPDATE and LedgerEntry INSERT are in one transaction.

    public async Task<AccountResponse> DepositAsync(Guid accountId, DepositRequest request, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            var account = await FindOrThrowAsync(accountId, ct);
            account.Deposit(request.Amount);

            var entry = LedgerEntry.CreateDeposit(
                accountId, request.Amount, account.Balance, request.Description);
            _db.LedgerEntries.Add(entry);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Deposited {Amount} into account {AccountId}", request.Amount, accountId);
            return MapToResponse(account);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    
    /// Withdraws money using pessimistic row locking (FOR UPDATE).
    ///
    /// Isolation: SELECT ... FOR UPDATE acquires an exclusive lock on the row.
    /// Any other transaction that tries to modify this account row will WAIT until
    /// our transaction commits or rolls back.  This prevents the lost-update race condition
    /// where two concurrent withdrawals both read the same (unreduced) balance and both
    /// decide they have enough funds.

    public async Task<AccountResponse> WithdrawAsync(Guid accountId, WithdrawRequest request, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            // Pessimistic locking: lock the row at read time.
            // "xmin, *" ensures the xmin concurrency token is also selected.
            var account = await _db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {accountId} FOR UPDATE")
                .AsTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new AccountNotFoundException(accountId);

            account.Withdraw(request.Amount);

            var entry = LedgerEntry.CreateWithdrawal(
                accountId, request.Amount, account.Balance, request.Description);
            _db.LedgerEntries.Add(entry);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Withdrew {Amount} from account {AccountId}", request.Amount, accountId);
            return MapToResponse(account);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<AccountResponse> CloseAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            var account = await _db.Accounts
                .FromSqlInterpolated(
                    $"SELECT xmin, * FROM accounts WHERE id = {accountId} FOR UPDATE")
                .AsTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new AccountNotFoundException(accountId);

            account.Close();

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Account {AccountId} closed", accountId);
            return MapToResponse(account);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private async Task<Account> FindOrThrowAsync(Guid accountId, CancellationToken ct)
    {
        return await _db.Accounts.FindAsync(new object[] { accountId }, ct)
            ?? throw new AccountNotFoundException(accountId);
    }

    public static AccountResponse MapToResponse(Account a) =>
        new(a.Id, a.OwnerName, a.Balance, a.Status.ToString(), a.CreatedAtUtc, a.ClosedAtUtc);
}
