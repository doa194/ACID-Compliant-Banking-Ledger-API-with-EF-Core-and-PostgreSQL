namespace BankingLedger.ConcurrencyTests.Infrastructure;

// xUnit collection: shares one PostgreSQL container across all concurrency tests
// while ensuring tests run sequentially (important for a shared DB state).
[CollectionDefinition("ConcurrencyTests")]
public sealed class ConcurrencyTestCollection : ICollectionFixture<PostgreSqlContainerFixture> { }
