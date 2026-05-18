using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BankingLedger.Infrastructure.Persistence;

/// Design-time factory used by the EF Core tooling (dotnet ef migrations add) when the
/// application cannot be started (e.g. during CI). It provides a DbContext instance
/// configured with a local PostgreSQL connection string so migration scaffolding works
/// without a running API host.
public sealed class BankingLedgerDbContextFactory : IDesignTimeDbContextFactory<BankingLedgerDbContext>
{
    public BankingLedgerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BankingLedgerDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=banking_ledger;Username=postgres;Password=postgres");
        return new BankingLedgerDbContext(optionsBuilder.Options);
    }
}
