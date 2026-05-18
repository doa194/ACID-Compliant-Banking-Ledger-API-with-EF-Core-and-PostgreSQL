using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BankingLedger.ConcurrencyTests.Infrastructure;

/// <summary>
/// Shared PostgreSQL container fixture for all concurrency tests.
/// Concurrency tests require a real PostgreSQL instance — you cannot test row locking,
/// xmin concurrency tokens, or transaction serialisation with an in-memory database.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("banking_ledger_concurrency_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public BankingLedgerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BankingLedgerDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new BankingLedgerDbContext(options);
    }
}
