using BankingLedger.Application.Accounts;
using BankingLedger.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankingLedger.Api.Controllers;

/// Exposes account lifecycle operations over HTTP.
/// Each action delegates to AccountService which owns the transaction logic.
[ApiController]
[Route("api/accounts")]
[Produces("application/json")]
public sealed class AccountsController : ControllerBase
{
    private readonly AccountService _accountService;

    public AccountsController(AccountService accountService)
    {
        _accountService = accountService;
    }

    /// Creates a new bank account.
    /// Atomicity: account creation and initial ledger entry are saved in one transaction.
    /// Returns 201 Created with Location header pointing to the new account.
    /// Returns 400 Bad Request if the request is invalid (e.g. negative initial deposit).
    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken ct)
    {
        var result = await _accountService.CreateAccountAsync(request, ct);
        return CreatedAtAction(nameof(GetAccount), new { accountId = result.Id }, result);
    }

    /// Returns a single account by ID.
    /// Returns 404 Not Found if the account doesn't exist and 200 OK with the account details if it does.
    /// Does not include the full ledger, just the account info and current balance.
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<AccountResponse> GetAccount(Guid accountId, CancellationToken ct) =>
        await _accountService.GetAccountAsync(accountId, ct);

    ///Returns the current balance for an account.
    [HttpGet("{accountId:guid}/balance")]
    [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<decimal> GetBalance(Guid accountId, CancellationToken ct) =>
        await _accountService.GetBalanceAsync(accountId, ct);

    /// Returns the full ledger (transaction history) for an account.
    [HttpGet("{accountId:guid}/ledger")]
    [ProducesResponseType(typeof(IReadOnlyList<LedgerEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<LedgerEntryResponse>> GetLedger(Guid accountId, CancellationToken ct) =>
        await _accountService.GetLedgerAsync(accountId, ct);

    
    /// Deposits money into an account.
    /// Atomicity: balance update and ledger entry are saved in one transaction.
    [HttpPost("{accountId:guid}/deposit")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<AccountResponse> Deposit(
        Guid accountId,
        [FromBody] DepositRequest request,
        CancellationToken ct) =>
        await _accountService.DepositAsync(accountId, request, ct);

    
    /// Withdraws money from an account.
    /// Isolation: uses SELECT ... FOR UPDATE (pessimistic locking) to prevent concurrent withdrawals
    /// from both reading the same pre-deduction balance.
    [HttpPost("{accountId:guid}/withdraw")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<AccountResponse> Withdraw(
        Guid accountId,
        [FromBody] WithdrawRequest request,
        CancellationToken ct) =>
        await _accountService.WithdrawAsync(accountId, request, ct);

    /// Permanently closes an account. No further transactions will be accepted.
    [HttpPost("{accountId:guid}/close")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<AccountResponse> CloseAccount(Guid accountId, CancellationToken ct) =>
        await _accountService.CloseAccountAsync(accountId, ct);
}
