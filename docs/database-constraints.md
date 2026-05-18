# Database Constraints

Database constraints enforce Consistency at the storage layer, independent of the application.
They are the last line of defence — even if there is a bug in the application code that
bypasses validation, the database will reject the write.

## Why constraints in the database AND the application?

The application layer (domain methods + FluentValidation) provides:
- Specific, user-friendly error messages
- Business logic context (e.g., "Insufficient funds" vs. a generic constraint error)
- Prevention of invalid writes before they reach the database

The database layer provides:
- Protection against application bugs
- Protection against direct database access (scripts, admin tools, other services)
- A source of truth that cannot be overridden in code

**Both layers together** implement the Consistency guarantee in ACID.

## Constraints in this project

### accounts table

| Constraint | SQL | Purpose |
|---|---|---|
| `CK_accounts_balance_non_negative` | `balance >= 0` | A negative balance is impossible in real banking. The application's InsufficientFundsException is the first check; this constraint is the second. |
| `CK_accounts_owner_name_not_empty` | `owner_name <> ''` | An account must belong to someone. The application validates this via FluentValidation. |
| `CK_accounts_status_valid` | `status IN ('Active', 'Closed', 'Frozen')` | Only the three known lifecycle states are valid. |

### transfers table

| Constraint | SQL | Purpose |
|---|---|---|
| `CK_transfers_amount_positive` | `amount > 0` | A zero or negative transfer amount is meaningless. |
| `CK_transfers_different_accounts` | `from_account_id <> to_account_id` | Transferring to yourself is a business rule violation that the database independently enforces. |
| `CK_transfers_status_valid` | `status IN ('Pending', 'Completed', 'Failed', 'RolledBack')` | Only known transfer lifecycle states. |

### ledger_entries table

| Constraint | SQL | Purpose |
|---|---|---|
| `CK_ledger_entries_amount_positive` | `amount > 0` | Every ledger entry records a real, positive movement of money. |
| `CK_ledger_entries_type_valid` | `type IN ('Deposit', 'Withdrawal', 'TransferDebit', 'TransferCredit')` | Only the four known ledger entry types. |

## How constraints are defined in EF Core

Constraints are specified in entity type configurations using the Fluent API:

```csharp
builder.ToTable(t =>
{
    t.HasCheckConstraint("CK_accounts_balance_non_negative", "balance >= 0");
    t.HasCheckConstraint("CK_accounts_owner_name_not_empty", "owner_name <> ''");
    t.HasCheckConstraint("CK_accounts_status_valid",
        "status IN ('Active', 'Closed', 'Frozen')");
});
```

EF Core migrations translate these into:
```sql
ALTER TABLE accounts
  ADD CONSTRAINT "CK_accounts_balance_non_negative" CHECK (balance >= 0),
  ADD CONSTRAINT "CK_accounts_owner_name_not_empty" CHECK (owner_name <> ''),
  ADD CONSTRAINT "CK_accounts_status_valid" CHECK (status IN ('Active', 'Closed', 'Frozen'));
```

## What happens when a constraint is violated

PostgreSQL raises an error of type `23514` (CHECK violation) with a message like:
```
ERROR: new row for relation "accounts" violates check constraint "CK_accounts_balance_non_negative"
DETAIL: Failing row contains (..., -50.00, ...).
```

In EF Core, this surfaces as a `DbUpdateException` with a PostgreSQL-specific inner exception.
The `ExceptionHandlingMiddleware` catches this and returns HTTP 500, which signals that
something very unexpected occurred (the application should have caught this at the domain layer).

## Enum constraints vs. PostgreSQL enum types

We store enum values as `varchar(30)` strings rather than as PostgreSQL `ENUM` types because:
1. Adding new enum values to a PostgreSQL ENUM requires an `ALTER TYPE` command and can
   cause table rewrites in some versions.
2. String columns are immediately readable without joining to a type definition.
3. CHECK constraints on strings are equally strict but much simpler to evolve.

## Indexes

Indexes are not constraints (they don't reject invalid data), but they enforce performance
requirements which are part of the system's correctness from a user perspective:

| Index | Columns | Purpose |
|---|---|---|
| `IX_ledger_entries_account_id_created_at_utc` | `(account_id, created_at_utc)` | Fast ledger history queries: "all entries for account X in date order" |
| `IX_transfers_from_account_id` | `from_account_id` | Fast lookup of outgoing transfers |
| `IX_transfers_to_account_id` | `to_account_id` | Fast lookup of incoming transfers |
| `IX_transfers_created_at_utc` | `created_at_utc` | Fast chronological transfer queries |
