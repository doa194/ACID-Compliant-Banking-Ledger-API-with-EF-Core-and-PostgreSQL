using BankingLedger.Application.DemoScenarios;
using BankingLedger.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankingLedger.Api.Controllers;


/// Provides educational ACID demonstration endpoints.
/// Each endpoint creates a self-contained scenario, executes it, and returns a structured
/// explanation of which ACID principle was demonstrated and what the outcome proves.
[ApiController]
[Route("api/demo")]
[Produces("application/json")]
public sealed class DemoController : ControllerBase
{
    private readonly DemoService _demoService;

    public DemoController(DemoService demoService)
    {
        _demoService = demoService;
    }
    /// ATOMICITY DEMO — Shows that a failing transfer rolls back all changes.
    /// Scenario: transfer begins, sender is debited, then an exception fires before
    /// the receiver is credited. RollbackAsync() restores both balances.
    /// Expected: both balances unchanged, zero ledger entries created.
    [HttpPost("atomicity/failed-transfer")]
    [ProducesResponseType(typeof(DemoResponse), StatusCodes.Status200OK)]
    public async Task<DemoResponse> AtomicityFailedTransfer(CancellationToken ct) =>
        await _demoService.DemonstrateAtomicityAsync(ct);

    /// CONSISTENCY DEMO — Shows that a negative balance is blocked by both application
    /// and database constraints. Attempts to withdraw more than the available balance.
    /// Expected: request fails, balance stays non-negative.
    [HttpPost("consistency/negative-balance")]
    [ProducesResponseType(typeof(DemoResponse), StatusCodes.Status200OK)]
    public async Task<DemoResponse> ConsistencyNegativeBalance(CancellationToken ct) =>
        await _demoService.DemonstrateConsistencyAsync(ct);

    /// ISOLATION DEMO — Shows how pessimistic row locking serialises concurrent withdrawals.
    /// Two withdrawals of 70 are attempted on an account with balance 100.
    /// Expected: only one succeeds, preventing the balance from going negative.
    /// For real parallel concurrency, see the ConcurrencyTests project.
    [HttpPost("isolation/concurrent-withdrawals")]
    [ProducesResponseType(typeof(DemoResponse), StatusCodes.Status200OK)]
    public async Task<DemoResponse> IsolationConcurrentWithdrawals(CancellationToken ct) =>
        await _demoService.DemonstrateIsolationAsync(ct);

    
    /// ISOLATION DEMO — Same as above but framed as concurrent transfers.
    [HttpPost("isolation/concurrent-transfers")]
    [ProducesResponseType(typeof(DemoResponse), StatusCodes.Status200OK)]
    public async Task<DemoResponse> IsolationConcurrentTransfers(CancellationToken ct) =>
        await _demoService.DemonstrateIsolationAsync(ct);

    /// DURABILITY DEMO — Creates a committed transfer and returns instructions for
    /// verifying that it persists after a container restart.
    /// Expected: the transfer record and ledger entries survive a PostgreSQL restart.
    [HttpPost("durability/create-committed-transfer")]
    [ProducesResponseType(typeof(DemoResponse), StatusCodes.Status200OK)]
    public async Task<DemoResponse> DurabilityCreateCommittedTransfer(CancellationToken ct) =>
        await _demoService.CreateCommittedTransferAsync(ct);
}
