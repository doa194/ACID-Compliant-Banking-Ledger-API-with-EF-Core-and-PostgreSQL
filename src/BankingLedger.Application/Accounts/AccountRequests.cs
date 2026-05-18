namespace BankingLedger.Application.Accounts;

/// Input for creating a new account.
public record CreateAccountRequest(string OwnerName, decimal InitialBalance);

/// Input for depositing money into an account.
public record DepositRequest(decimal Amount, string Description);

/// Input for withdrawing money from an account.
public record WithdrawRequest(decimal Amount, string Description);
