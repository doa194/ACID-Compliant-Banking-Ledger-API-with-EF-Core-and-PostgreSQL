using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Ledger;
using BankingLedger.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace BankingLedger.Infrastructure.Persistence;


/// The EF Core "unit of work" — the entry point for all database operations.
///
/// DbContext tracks every entity that has been loaded or created in the current scope.
/// When SaveChangesAsync() is called, EF Core translates all pending changes into SQL
/// (INSERT, UPDATE, DELETE) and executes them in a single batch.
///
/// In this application we wrap SaveChangesAsync() in explicit transactions
/// (BeginTransactionAsync / CommitAsync / RollbackAsync) so that multiple SaveChanges
/// calls — for example, updating two account balances and inserting two ledger entries —
/// are all treated as one atomic unit.
public sealed class BankingLedgerDbContext : DbContext
{
    public BankingLedgerDbContext(DbContextOptions<BankingLedgerDbContext> options)
        : base(options) { }

    //All bank accounts. Maps to the "accounts" table.
    public DbSet<Account> Accounts => Set<Account>();

    //All money transfers. Maps to the "transfers" table.
    public DbSet<Transfer> Transfers => Set<Transfer>();

    //The immutable audit trail. Maps to the "ledger_entries" table.
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Automatically discover and apply every IEntityTypeConfiguration<T> defined in
        // this assembly (AccountConfiguration, TransferConfiguration, LedgerEntryConfiguration).
        // This keeps OnModelCreating clean and lets each entity own its own configuration.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankingLedgerDbContext).Assembly);
    }
}
