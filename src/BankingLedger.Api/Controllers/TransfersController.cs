using BankingLedger.Application.Transfers;
using BankingLedger.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankingLedger.Api.Controllers;
/// Exposes money transfer operations over HTTP.
/// Transfers are the primary demonstration of all four ACID properties simultaneously.
[ApiController]
[Route("api/transfers")]
[Produces("application/json")]
public sealed class TransfersController : ControllerBase
{
    private readonly TransferService _transferService;

    public TransfersController(TransferService transferService)
    {
        _transferService = transferService;
    } 
    /// Initiates a money transfer between two accounts.
    /// All six steps (lock sender, lock receiver, debit, credit, insert transfer,
    /// insert two ledger entries) happen atomically in one transaction.

    [HttpPost]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferRequest request,
        CancellationToken ct)
    {
        var result = await _transferService.TransferAsync(request, ct);
        return CreatedAtAction(nameof(GetTransfer), new { transferId = result.Id }, result);
    }

    /// Returns a single transfer record by ID.
    [HttpGet("{transferId:guid}")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<TransferResponse> GetTransfer(Guid transferId, CancellationToken ct) =>
        await _transferService.GetTransferAsync(transferId, ct);

    /// Returns all transfers ordered by creation date descending.
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TransferResponse>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<TransferResponse>> GetTransfers(CancellationToken ct) =>
        await _transferService.GetTransfersAsync(ct);
}
