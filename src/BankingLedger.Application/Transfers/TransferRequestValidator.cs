using FluentValidation;

namespace BankingLedger.Application.Transfers;

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.FromAccountId)
            .NotEmpty().WithMessage("Source account ID is required.");

        RuleFor(x => x.ToAccountId)
            .NotEmpty().WithMessage("Destination account ID is required.")
            .NotEqual(x => x.FromAccountId).WithMessage("Source and destination accounts must be different.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Transfer amount must be greater than zero.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(300).WithMessage("Description must not exceed 300 characters.");
    }
}
