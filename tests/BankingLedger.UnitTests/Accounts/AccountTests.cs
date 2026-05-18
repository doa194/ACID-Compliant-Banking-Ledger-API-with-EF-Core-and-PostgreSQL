using BankingLedger.Domain.Accounts;
using BankingLedger.Domain.Exceptions;
using FluentAssertions;

namespace BankingLedger.UnitTests.Accounts;

/// <summary>
/// Pure unit tests for the Account domain entity.
/// No database or I/O is involved — these test only the in-memory business rules.
/// Fast, deterministic, and runnable offline.
/// </summary>
public sealed class AccountTests
{
    // ── Create ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveAccount()
    {
        var account = Account.Create("Alice", initialBalance: 500m);

        account.Id.Should().NotBe(Guid.Empty);
        account.OwnerName.Should().Be("Alice");
        account.Balance.Should().Be(500m);
        account.Status.Should().Be(AccountStatus.Active);
        account.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
        account.ClosedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOwnerName_ThrowsDomainException(string? name)
    {
        // Consistency: owner name cannot be blank.
        var act = () => Account.Create(name!, 100m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativeBalance_ThrowsDomainException()
    {
        // Consistency: opening balance cannot be negative.
        var act = () => Account.Create("Bob", initialBalance: -1m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithZeroBalance_Succeeds()
    {
        // Zero is a valid opening balance — some accounts start empty.
        var account = Account.Create("Charlie", initialBalance: 0m);
        account.Balance.Should().Be(0m);
    }

    // ── Deposit ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Deposit_PositiveAmount_IncreasesBalance()
    {
        var account = Account.Create("Alice", 100m);
        account.Deposit(50m);
        account.Balance.Should().Be(150m);
    }

    [Fact]
    public void Deposit_ZeroAmount_ThrowsDomainException()
    {
        var account = Account.Create("Alice", 100m);
        var act = () => account.Deposit(0m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deposit_NegativeAmount_ThrowsDomainException()
    {
        var account = Account.Create("Alice", 100m);
        var act = () => account.Deposit(-10m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deposit_IntoClosedAccount_ThrowsAccountNotActiveException()
    {
        // Consistency: closed accounts are immutable.
        var account = Account.Create("Alice", 100m);
        account.Close();
        var act = () => account.Deposit(50m);
        act.Should().Throw<AccountNotActiveException>();
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Withdraw_SufficientFunds_DecreasesBalance()
    {
        var account = Account.Create("Alice", 100m);
        account.Withdraw(40m);
        account.Balance.Should().Be(60m);
    }

    [Fact]
    public void Withdraw_ExactBalance_LeavesZero()
    {
        // Edge case: withdrawing the entire balance is allowed.
        var account = Account.Create("Alice", 100m);
        account.Withdraw(100m);
        account.Balance.Should().Be(0m);
    }

    [Fact]
    public void Withdraw_InsufficientFunds_ThrowsInsufficientFundsException()
    {
        // Consistency: balance must never go negative.
        // This is the application's first line of defence; the DB constraint is the second.
        var account = Account.Create("Alice", 50m);
        var act = () => account.Withdraw(100m);
        act.Should().Throw<InsufficientFundsException>()
            .Which.CurrentBalance.Should().Be(50m);
    }

    [Fact]
    public void Withdraw_ZeroAmount_ThrowsDomainException()
    {
        var account = Account.Create("Alice", 100m);
        var act = () => account.Withdraw(0m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Withdraw_FromClosedAccount_ThrowsAccountNotActiveException()
    {
        var account = Account.Create("Alice", 100m);
        account.Close();
        var act = () => account.Withdraw(10m);
        act.Should().Throw<AccountNotActiveException>();
    }

    // ── Debit / Credit (used by transfers) ───────────────────────────────────────────────────────

    [Fact]
    public void Debit_InsufficientFunds_ThrowsInsufficientFundsException()
    {
        var account = Account.Create("Alice", 30m);
        var act = () => account.Debit(50m);
        act.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void Credit_PositiveAmount_IncreasesBalance()
    {
        var account = Account.Create("Bob", 0m);
        account.Credit(100m);
        account.Balance.Should().Be(100m);
    }

    // ── Close ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_ActiveAccount_SetsStatusAndTimestamp()
    {
        var account = Account.Create("Alice", 100m);
        account.Close();

        account.Status.Should().Be(AccountStatus.Closed);
        account.ClosedAtUtc.Should().NotBeNull();
        account.ClosedAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Close_AlreadyClosedAccount_ThrowsAccountNotActiveException()
    {
        // Closing a closed account is an invalid state transition.
        var account = Account.Create("Alice", 0m);
        account.Close();
        var act = () => account.Close();
        act.Should().Throw<AccountNotActiveException>();
    }

    // ── Multi-step scenarios ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleDepositsAndWithdrawals_ProduceCorrectBalance()
    {
        var account = Account.Create("Alice", 1000m);
        account.Deposit(500m);   // 1500
        account.Withdraw(200m);  // 1300
        account.Deposit(100m);   // 1400
        account.Withdraw(400m);  // 1000
        account.Balance.Should().Be(1000m);
    }
}
