using FluentValidation;

namespace BankingLedger.Application.Accounts;

/// Validates the request to create an account before it reaches the service layer.
/// FluentValidation runs these rules automatically when the controller action is invoked,
/// returning a 400 Bad Request with structured error details if validation fails.
/// This keeps our service layer clean and focused on business logic, while ensuring that incoming data is well-formed and meets our requirements.
public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.OwnerName)
            .NotEmpty().WithMessage("Owner name is required.")
            .MaximumLength(150).WithMessage("Owner name must not exceed 150 characters.");

        RuleFor(x => x.InitialBalance)
            .GreaterThanOrEqualTo(0).WithMessage("Initial balance cannot be negative.");
    }
}

public sealed class DepositRequestValidator : AbstractValidator<DepositRequest>
{
    public DepositRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Deposit amount must be greater than zero.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(300).WithMessage("Description must not exceed 300 characters.");
    }
}

public sealed class WithdrawRequestValidator : AbstractValidator<WithdrawRequest>
{
    public WithdrawRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Withdrawal amount must be greater than zero.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(300).WithMessage("Description must not exceed 300 characters.");
    }
}
