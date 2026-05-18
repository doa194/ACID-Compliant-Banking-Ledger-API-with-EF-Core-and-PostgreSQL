using BankingLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BankingLedger.Infrastructure;

/// Extension method to register all infrastructure dependencies in the DI container.
/// Called once from Program.cs — keeps the API project free from infrastructure details.
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Connection string 'Postgres' is missing from configuration.");

        // Register BankingLedgerDbContext as a scoped service.
        // Scoped means one DbContext instance per HTTP request — this is the standard
        // ASP.NET Core pattern. Each request gets its own change-tracker and transaction scope.
        services.AddDbContext<BankingLedgerDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Retry logic: if the connection is temporarily refused (e.g. PostgreSQL is
                // still starting up), automatically retry up to 5 times with exponential backoff.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });

            // In development, log every SQL statement that EF Core generates.
            // This is invaluable for understanding how transactions and locks translate to SQL.
            options.EnableSensitiveDataLogging(
                sensitiveDataLoggingEnabled: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development");
        });

        return services;
    }
}
