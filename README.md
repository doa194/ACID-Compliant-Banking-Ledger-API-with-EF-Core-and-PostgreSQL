# Transactional Banking Ledger API

A portfolio-grade .NET 10 Web API that demonstrates **ACID database principles** using a
realistic banking ledger domain. The focus is not on building a production bank — it is on
proving, in executable code, that transactions are correct, concurrent, and durable.

---

## Why This Project Exists

Most tutorials show how to CRUD data. Few demonstrate what happens when things go wrong
simultaneously:

- What if two users withdraw from the same account at the same moment?
- What if a transfer crashes halfway through?
- What if the server restarts immediately after a commit?

This project answers each question with real PostgreSQL transactions, row locks, concurrency
tokens, and integration tests that run against a genuine database.

---

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10, C# |
| Web framework | ASP.NET Core Web API |
| ORM | Entity Framework Core 9 |
| Database | PostgreSQL 17 |
| Containerisation | Docker Compose |
| Logging | Serilog (structured JSON) |
| API docs | Scalar + OpenAPI |
| Validation | FluentValidation |
| Testing | xUnit, Testcontainers, FluentAssertions |

---

## Architecture

```
transactional-banking-ledger-api/
├── src/
│   ├── BankingLedger.Domain/          Pure domain: entities, enums, exceptions
│   ├── BankingLedger.Application/     Use cases: AccountService, TransferService, DemoService
│   ├── BankingLedger.Infrastructure/  EF Core: DbContext, configurations, migrations
│   └── BankingLedger.Api/             ASP.NET Core: controllers, middleware, Program.cs
│
└── tests/
    ├── BankingLedger.UnitTests/        Domain entity business rules (no DB)
    ├── BankingLedger.IntegrationTests/ Full service tests against real PostgreSQL
    └── BankingLedger.ConcurrencyTests/ Parallel Task.WhenAll tests proving isolation
```

Light clean architecture: each layer depends only on the layer below it.
No MediatR, no CQRS, no over-engineering — the domain and transactions are the point.

---

## ACID Principles Demonstrated

### Atomicity — "All or nothing"

A transfer has six steps. They all commit together or none of them do.

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
try
{
    sender.Debit(amount);
    receiver.Credit(amount);
    db.Transfers.Add(transfer);
    db.LedgerEntries.AddRange(debitEntry, creditEntry);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);      // ← all six writes become durable here
}
catch
{
    await transaction.RollbackAsync(ct);    // ← all six writes are undone here
    throw;
}
```

Demo: `POST /api/demo/atomicity/failed-transfer`
Test: `RollbackScenarioTests.ForcedRollback_BetweenDebitAndCredit_LeavesNeitherBalanceChanged`

### Consistency — "Valid state to valid state"

Two layers enforce the rules:

1. **Application layer** — domain methods throw before touching the database
2. **Database layer** — PostgreSQL CHECK constraints block invalid writes independently

```
balance >= 0          ← enforced by InsufficientFundsException AND CHECK constraint
owner_name <> ''      ← enforced by FluentValidation AND CHECK constraint
amount > 0            ← enforced by validators AND CHECK constraint
```

Demo: `POST /api/demo/consistency/negative-balance`
Test: `RollbackScenarioTests.DatabaseConstraint_BlocksNegativeBalanceViaEfCore`

### Isolation — "Concurrent transactions don't corrupt each other"

**Pessimistic locking** (FOR UPDATE) for high-contention writes:
```sql
SELECT xmin, * FROM accounts WHERE id = @id FOR UPDATE
```
The row is locked from read until commit. Other writers must wait.

**Deadlock prevention** via ordered locking: accounts are always locked in GUID order,
so concurrent transfers between the same pair of accounts always queue rather than deadlock.

**Optimistic concurrency** (xmin token) for all account updates:
```csharp
builder.UseXminAsConcurrencyToken(); // EF Core adds "AND xmin = @original" to every UPDATE
```
If xmin changed, EF Core throws `DbUpdateConcurrencyException` → HTTP 409.

Demo: `POST /api/demo/isolation/concurrent-withdrawals`
Test: `ConcurrentWithdrawalTests` — 10 parallel withdrawals, only 3 succeed

### Durability — "Committed data survives restarts"

PostgreSQL's Write-Ahead Log (WAL) flushes every committed transaction to disk before
returning the commit acknowledgement. A server crash immediately after `CommitAsync()`
returns leaves the data intact.

Manual proof:
1. `POST /api/demo/durability/create-committed-transfer` — note the `transferId`
2. `docker-compose stop postgres && docker-compose start postgres`
3. `GET /api/transfers/{transferId}` — the record is still there

---

## Core Features

- **Account management** — create, get, deposit, withdraw, close
- **Transfers** — atomic six-step transfers with ordered pessimistic locking
- **Ledger** — immutable double-entry audit trail (TransferDebit + TransferCredit per transfer)
- **ACID demo endpoints** — structured responses explaining each principle
- **Structured logging** — Serilog logs every SQL statement in development

---

## Database Design

```sql
accounts (id, owner_name, balance, status, created_at_utc, closed_at_utc, xmin)
transfers (id, from_account_id, to_account_id, amount, status, created_at_utc, failure_reason)
ledger_entries (id, account_id, transfer_id, type, amount, balance_after_transaction, description, created_at_utc)
```

xmin is a PostgreSQL system column — it exists on every table automatically.

---

## Transaction Flow (Transfer)

```
BEGIN TRANSACTION (ReadCommitted)
  ├── LOCK smaller GUID account (FOR UPDATE)
  ├── LOCK larger GUID account (FOR UPDATE)
  ├── Validate: both active, sender has funds
  ├── UPDATE accounts SET balance = balance - @amount WHERE id = @senderId
  ├── UPDATE accounts SET balance = balance + @amount WHERE id = @receiverId
  ├── INSERT INTO transfers VALUES (...)
  ├── INSERT INTO ledger_entries VALUES (...) -- TransferDebit
  ├── INSERT INTO ledger_entries VALUES (...) -- TransferCredit
  └── COMMIT → all 6 writes durable, locks released
```

---

## Running Locally

```bash
# Start PostgreSQL
docker-compose up -d

# Apply schema
cd src/BankingLedger.Api
dotnet ef database update --project ../BankingLedger.Infrastructure

# Run the API
dotnet run

# Open API docs
# Navigate to: https://localhost:7xxx/scalar/v1
```

---

## API Examples

```bash
# Create account
curl -X POST http://localhost:5000/api/accounts \
  -H "Content-Type: application/json" \
  -d '{"ownerName": "Alice", "initialBalance": 1000}'

# Deposit
curl -X POST http://localhost:5000/api/accounts/{id}/deposit \
  -H "Content-Type: application/json" \
  -d '{"amount": 500, "description": "Salary"}'

# Transfer
curl -X POST http://localhost:5000/api/transfers \
  -H "Content-Type: application/json" \
  -d '{"fromAccountId": "...", "toAccountId": "...", "amount": 200, "description": "Rent"}'

# Demo: Atomicity (forced rollback)
curl -X POST http://localhost:5000/api/demo/atomicity/failed-transfer
```

---

## Test Scenarios

| Test | What it proves |
|---|---|
| `AccountTests.Withdraw_InsufficientFunds` | Application Consistency |
| `AccountIntegrationTests.Deposit_IncreasesBalanceAndCreatesLedgerEntry` | Atomicity of deposit |
| `TransferIntegrationTests.Transfer_MovesMoneyAndCreatesDoubleLedgerEntries` | Full transfer atomicity |
| `RollbackScenarioTests.ForcedRollback_BetweenDebitAndCredit` | Atomicity via rollback |
| `RollbackScenarioTests.DatabaseConstraint_BlocksNegativeBalanceViaEfCore` | DB-level Consistency |
| `ConcurrentWithdrawalTests.TenConcurrentWithdrawals_OnlyThreeSucceed` | Isolation via FOR UPDATE |
| `ConcurrentTransferTests.FiveConcurrentTransfers_OnlyTwoSucceed` | Isolation + deadlock prevention |

---

## Key Learning Outcomes

After studying this project you will understand:

- Why `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` are essential for multi-step operations
- How PostgreSQL's `xmin` enables optimistic concurrency detection without locks
- How `SELECT ... FOR UPDATE` prevents the lost-update problem
- Why locking in GUID order prevents deadlocks
- Why database constraints are still needed even when the application validates inputs
- How Testcontainers makes integration tests reproducible without a shared database

---