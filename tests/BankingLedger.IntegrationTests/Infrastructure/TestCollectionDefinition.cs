namespace BankingLedger.IntegrationTests.Infrastructure;

// xUnit collection: ensures all integration tests in this project share ONE container
// instance (via PostgreSqlContainerFixture) rather than each starting their own.
// Tests in the same collection run sequentially — important so tests do not interfere
// with each other's database state.
[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture> { }
