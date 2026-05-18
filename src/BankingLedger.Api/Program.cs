using BankingLedger.Api.Middleware;
using BankingLedger.Application.Accounts;
using BankingLedger.Application.Transfers;
using BankingLedger.Infrastructure;
using BankingLedger.Infrastructure.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Scalar.AspNetCore;
using Serilog;

// Bootstrap Serilog 
// Serilog writes structured (key=value) log entries instead of plain text strings.
// Structured logs are machine-readable, searchable, and easy to ship to aggregators like Seq or ELK. That's why we use Serilog instead of the built-in ASP.NET Core logger. Other benefits include:
// - Enrichers: add useful properties to every log entry, like request IDs, user IDs, or custom tags.
// - Sinks: write logs to various destinations (console, files, databases, log aggregators) with different formats and configurations.
// - Configuration: control log levels, output formats, and enrichers via code or configuration files without changing application code.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/banking-ledger.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Banking Ledger API");

    var builder = WebApplication.CreateBuilder(args);

    // Replace the built-in ASP.NET Core logger with Serilog so every log source
    // (controllers, EF Core SQL logs, middleware) flows through Serilog.
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console()
           .WriteTo.File("logs/banking-ledger.log", rollingInterval: RollingInterval.Day));

    // ── Services ──────────────────────────────────────────────────────────────────────────────────

    builder.Services.AddControllers();

    // OpenAPI: generates a machine-readable JSON schema of every endpoint at /openapi/v1.json.
    builder.Services.AddOpenApi();

    // FluentValidation: validates request bodies automatically via model binding pipeline.
    // When a validator exists for a request type, ASP.NET Core runs it before the action method.
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<CreateAccountRequestValidator>();
    builder.Services.AddValidatorsFromAssemblyContaining<TransferRequestValidator>();

    // Application services — scoped means one instance per HTTP request.
    // This matches DbContext's lifetime, which is also scoped.
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddScoped<TransferService>();
    builder.Services.AddScoped<DemoService>();

    // Infrastructure: registers BankingLedgerDbContext configured with Npgsql/PostgreSQL.
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── HTTP pipeline ─────────────────────────────────────────────────────────────────────────────

    var app = builder.Build();

    // Serilog request logging: logs every HTTP request with method, path, status, and duration.
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        // Serve the OpenAPI JSON schema at /openapi/v1.json.
        app.MapOpenApi();

        // Scalar: modern interactive API documentation UI at /scalar/v1.
        // Navigate here to explore and call every endpoint without needing an external client.
        app.MapScalarApiReference();
    }

    // Global exception handler — converts domain and EF Core exceptions to RFC 7807 ProblemDetails.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseHttpsRedirection();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make the Program class visible to WebApplicationFactory<Program> in integration tests.
// Without this, test projects cannot reference Program to spin up the API in-process.
public partial class Program { }
