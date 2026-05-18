# ACID Principles in This Project

ACID is a set of four guarantees that a database system makes about transactions.
A **transaction** is a group of database operations that are treated as a single unit.

---

## A — Atomicity

**"All or nothing."**

If a transaction contains multiple steps, either all of them succeed or none of them do.
There is no such thing as a "half-completed" transaction in a correct database system.

### How it's demonstrated here

A money transfer has six steps:
1. Lock sender row (FOR UPDATE)
2. Lock receiver row (FOR UPDATE)
3. Debit sender balance
4. Credit receiver balance
5. Insert Transfer record
6. Insert two LedgerEntry rows

All six steps happen inside one `BeginTransactionAsync` / `CommitAsync` block.

If step 4 throws (e.g., receiver account is closed), `RollbackAsync()` is called and
steps 3, 5, 6 are **automatically undone** — the sender's balance is restored and no
transfer record or ledger entries are created.

### Where to find it in code

- `TransferService.TransferAsync` — the main transfer transaction
- `DemoService.DemonstrateAtomicityAsync` — shows a forced rollback
- `RollbackScenarioTests.ForcedRollback_BetweenDebitAndCredit_LeavesNeitherBalanceChanged`
- `POST /api/demo/atomicity/failed-transfer`

---

## C — Consistency

**"The database moves from one valid state to another valid state."**

Consistency means you cannot write data that violates the rules of the system.
These rules are enforced in two layers:

1. **Application layer** — domain methods validate before writing
2. **Database layer** — CHECK constraints reject invalid data even if the app has a bug

### Application rules enforced here

| Rule | Enforced by |
|---|---|
| Amount > 0 | `DepositRequestValidator`, `WithdrawRequestValidator`, `Account.Deposit/Withdraw` |
| Sender ≠ Receiver | `TransferRequestValidator`, `TransferService` |
| Both accounts must be Active | `Account.EnsureActive()` |
| Sender must have sufficient funds | `Account.Debit()` → `InsufficientFundsException` |

### Database constraints enforced here

| Table | Constraint | SQL |
|---|---|---|
| accounts | Balance non-negative | `balance >= 0` |
| accounts | Owner name not empty | `owner_name <> ''` |
| accounts | Valid status | `status IN ('Active', 'Closed', 'Frozen')` |
| transfers | Positive amount | `amount > 0` |
| transfers | Different accounts | `from_account_id <> to_account_id` |
| transfers | Valid status | `status IN ('Pending', 'Completed', 'Failed', 'RolledBack')` |
| ledger_entries | Positive amount | `amount > 0` |
| ledger_entries | Valid type | `type IN ('Deposit', 'Withdrawal', 'TransferDebit', 'TransferCredit')` |

### Where to find it in code

- `AccountConfiguration.cs`, `TransferConfiguration.cs`, `LedgerEntryConfiguration.cs` — DB constraints
- `Account.Withdraw()` — InsufficientFundsException before the database is touched
- `DemoService.DemonstrateConsistencyAsync` — shows both layers blocking a negative balance
- `POST /api/demo/consistency/negative-balance`

---

## I — Isolation

**"Concurrent transactions must not corrupt each other."**

When two transactions run simultaneously, each one should behave as if it were the
only transaction running. Without isolation, concurrent transactions can produce
incorrect results:

- **Dirty read**: reading data another transaction hasn't committed yet
- **Lost update**: two transactions both read a value, both modify it, and the second
  write silently overwrites the first
- **Phantom read**: a query returns different rows when run twice in the same transaction

### Two isolation strategies used here

#### 1. Pessimistic Locking (FOR UPDATE)

Used in: withdrawals, transfers, account closing.

```sql
SELECT xmin, * FROM accounts WHERE id = @id FOR UPDATE
```

PostgreSQL locks the row at read time. Any other transaction that tries to modify this
row must wait until the lock is released (at commit or rollback). This prevents the
lost-update problem for high-contention operations.

**Ordered locking for transfers**: to prevent deadlocks, accounts are always locked in
the same order (smaller GUID first). This means two concurrent transfers involving the
same pair of accounts will always acquire locks in the same sequence and queue, rather
than each holding one lock and waiting for the other's (circular wait = deadlock).

#### 2. Optimistic Concurrency (xmin token)

Used by EF Core automatically for all account updates.

PostgreSQL's `xmin` system column stores the transaction ID of the last write to a row.
EF Core reads `xmin` when loading an Account, then includes `AND xmin = @original_xmin`
in every UPDATE statement. If another transaction updated the row in between, `xmin`
changed and the UPDATE affects 0 rows — EF Core throws `DbUpdateConcurrencyException`.

Optimistic concurrency is appropriate when conflicts are rare. Pessimistic locking is
appropriate when conflicts are expected (e.g., a popular shared account).

### Where to find it in code

- `AccountService.WithdrawAsync` — FOR UPDATE before deducting
- `TransferService.TransferAsync` — ordered FOR UPDATE locking
- `AccountConfiguration.cs` — `UseXminAsConcurrencyToken()`
- `ExceptionHandlingMiddleware.cs` — maps `DbUpdateConcurrencyException` to HTTP 409
- `ConcurrentWithdrawalTests.cs`, `ConcurrentTransferTests.cs`

---

## D — Durability

**"Once a transaction commits, it stays committed."**

A committed transaction must survive:
- Application crashes
- Power failures
- Server restarts

PostgreSQL achieves this via the **Write-Ahead Log (WAL)**. Every change is written to
the WAL on disk _before_ the commit acknowledgement is sent back. If the server crashes
immediately after `CommitAsync()` returns, PostgreSQL replays the WAL on restart and
restores the committed state.

### How to manually verify Durability

1. Call `POST /api/demo/durability/create-committed-transfer`
2. Note the `transferId` in the response
3. Stop the PostgreSQL container: `docker-compose stop postgres`
4. Restart it: `docker-compose start postgres`
5. Call `GET /api/transfers/{transferId}`
6. The transfer record and associated ledger entries will still be there

### Where to find it in code

- Every `CommitAsync()` call — the moment data becomes durable
- `DemoService.DemonstrateDurabilityAsync` — creates a committed transfer with instructions
- `POST /api/demo/durability/create-committed-transfer`
