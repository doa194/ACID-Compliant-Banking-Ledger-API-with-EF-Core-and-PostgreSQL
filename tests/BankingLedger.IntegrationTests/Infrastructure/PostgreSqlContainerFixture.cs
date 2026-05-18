using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BankingLedger.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit class fixture that starts a real PostgreSQL Docker container before any test in the
/// collection runs and stops it afterwards.
///
/// Why a real database?
/// In-memory databases do not enforce CHECK constraints, don't support FOR UPDATE locking,
/// and don't have xmin — so they cannot prove the ACID guarantees we care about.
/// Real PostgreSQL is essential for meaningful integration and concurrency tests.
///
/// Testcontainers pulls a postgres:17 Docker image, starts a container, waits for it to
/// be ready, and gives us a connection string pointing at it.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("banking_ledger_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Apply the EF Core schema to the test database.
        // EnsureCreatedAsync() generates CREATE TABLE statements from the model — it uses the
        // entity configurations (including CHECK constraints) to produce the correct schema.
        // This is faster than running migrations and appropriate for isolated test databases.
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a fresh DbContext pointed at the test container.
    /// Each test should create its own context so change-trackers don't interfere.
    /// </summary>
    public BankingLedgerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BankingLedgerDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new BankingLedgerDbContext(options);
    }
}
