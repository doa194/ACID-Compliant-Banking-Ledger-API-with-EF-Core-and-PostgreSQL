using BankingLedger.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BankingLedger.Api.Middleware;

/// Converts domain exceptions and EF Core concurrency exceptions into RFC 7807
/// ProblemDetails responses.  Centralising this here means controllers never need
/// try/catch blocks for these common error types.
/// Used a middleware for Exception Handling because it can catch exceptions thrown from any part of the pipeline, including model binding and other middleware, not just from controller actions. This ensures a consistent error handling strategy across the entire application.
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
            context.Request.Method, context.Request.Path);

        var problem = exception switch
        {
            // 404 — resource not found
            AccountNotFoundException or TransferNotFoundException => new ProblemDetails
            {
                Type = "https://example.com/problems/not-found",
                Title = "Resource not found",
                Status = StatusCodes.Status404NotFound,
                Detail = exception.Message
            },

            // 409 — insufficient funds (a business-rule conflict)
            InsufficientFundsException => new ProblemDetails
            {
                Type = "https://example.com/problems/insufficient-funds",
                Title = "Insufficient funds",
                Status = StatusCodes.Status409Conflict,
                Detail = exception.Message
            },

            // 409 — account not active
            AccountNotActiveException => new ProblemDetails
            {
                Type = "https://example.com/problems/account-not-active",
                Title = "Account not active",
                Status = StatusCodes.Status409Conflict,
                Detail = exception.Message
            },

            // 409 — optimistic concurrency conflict (xmin changed between read and write).
            // The client should retry the operation — the data they read may now be stale.
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Type = "https://example.com/problems/concurrency-conflict",
                Title = "Concurrency conflict",
                Status = StatusCodes.Status409Conflict,
                Detail =
                    "The account was modified by another transaction between your read and write. " +
                    "This is detected via PostgreSQL's xmin concurrency token. Please retry the operation."
            },

            // 422 — other domain rule violations
            DomainException => new ProblemDetails
            {
                Type = "https://example.com/problems/domain-error",
                Title = "Business rule violation",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = exception.Message
            },

            // 500 — unexpected error
            _ => new ProblemDetails
            {
                Type = "https://example.com/problems/internal-error",
                Title = "Internal server error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred. Please check the logs."
            }
        };

        context.Response.StatusCode = problem.Status ?? 500;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
