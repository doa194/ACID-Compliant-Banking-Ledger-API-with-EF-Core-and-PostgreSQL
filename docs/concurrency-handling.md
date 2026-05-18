# Concurrency Handling

Concurrency problems occur when two or more operations try to read and modify the same
data at the same time. Without protection, the result can be incorrect data, lost updates,
or phantom rows.

## The two strategies used in this project

### 1. Pessimistic Concurrency — `SELECT ... FOR UPDATE`

**Philosophy**: assume conflicts will happen, so prevent them upfront.

A `FOR UPDATE` clause tells PostgreSQL: "I am about to modify this row. Lock it now so
no other transaction can change it until I am done."

```sql
-- This acquires an exclusive row lock.
SELECT xmin, * FROM accounts WHERE id = @accountId FOR UPDATE;

-- The lock is held until COMMIT or ROLLBACK.
UPDATE accounts SET balance = balance - 100 WHERE id = @accountId;
COMMIT;
```

While this transaction holds the lock, any other transaction that tries to modify the
same row will **block (wait)** until the first transaction finishes. It won't fail —
it will just wait. This is different from optimistic concurrency, which fails immediately.

**Used for**: withdrawals, transfers, account closing.

**Configured in**: `AccountService.WithdrawAsync`, `TransferService.TransferAsync`.

**Trade-off**: higher throughput protection but lower parallelism — concurrent writers must queue.

### 2. Optimistic Concurrency — PostgreSQL `xmin`

**Philosophy**: assume conflicts are rare. Don't lock; detect conflicts when saving.

Every PostgreSQL row has a hidden system column called `xmin` (transaction ID of the last write).
PostgreSQL automatically updates `xmin` on every row modification — we never set it ourselves.

Npgsql EF Core provider reads `xmin` in every SELECT and includes it in every UPDATE:

```sql
-- EF Core generated SELECT (includes xmin automatically):
SELECT xmin, id, owner_name, balance, ...
FROM accounts WHERE id = @id;

-- EF Core generated UPDATE (checks xmin before writing):
UPDATE accounts
SET balance = @newBalance
WHERE id = @id AND xmin = @originalXmin;
```

If another transaction updated the row between our SELECT and UPDATE, `xmin` will have
changed and the UPDATE affects 0 rows. EF Core detects this and throws
`DbUpdateConcurrencyException`, which the `ExceptionHandlingMiddleware` maps to HTTP 409.

**Configured via**: `builder.UseXminAsConcurrencyToken()` in `AccountConfiguration.cs`.

**Trade-off**: no locking overhead, but requires retry logic when conflicts occur.

## Deadlock prevention via ordered locking

A deadlock occurs when:
- Transaction A holds a lock on row X and is waiting for a lock on row Y
- Transaction B holds a lock on row Y and is waiting for a lock on row X
- Neither can proceed — circular wait

**Solution**: always acquire locks in a consistent, predetermined order.

In `TransferService.TransferAsync`:
```csharp
// Compare GUIDs lexicographically — always lock the smaller one first.
var (firstId, secondId) = fromAccountId.CompareTo(toAccountId) < 0
    ? (fromAccountId, toAccountId)
    : (toAccountId, fromAccountId);

// Lock first account — smaller GUID always locked first.
var first = await db.Accounts
    .FromSqlInterpolated($"SELECT xmin, * FROM accounts WHERE id = {firstId} FOR UPDATE")
    .SingleAsync();

// Lock second account.
var second = await db.Accounts
    .FromSqlInterpolated($"SELECT xmin, * FROM accounts WHERE id = {secondId} FOR UPDATE")
    .SingleAsync();
```

With this ordering:
- Transfer(A→B) locks A first, then B
- Transfer(B→A) also locks A first (because A.Id < B.Id), then B
- Both always acquire locks in the same order → they queue, not deadlock

## Concurrency test scenarios

### Test 1: Concurrent Withdrawals

```
Given: balance = 100
When:  10 concurrent requests each withdraw 30
Expected: only 3 succeed (3×30=90 ≤ 100), balance = 10, no negative balance
```

`ConcurrentWithdrawalTests.TenConcurrentWithdrawals_OnlyThreeSucceed_BalanceNeverNegative`

### Test 2: Concurrent Transfers

```
Given: Account A = 100, Account B = 0
When:  5 concurrent transfers of 50 from A to B
Expected: only 2 succeed, A = 0, B = 100, 4 ledger entries
```

`ConcurrentTransferTests.FiveConcurrentTransfers_OnlyTwoSucceed_BothBalancesCorrect`

## What happens without concurrency protection

Without `FOR UPDATE` locking, this race condition is possible:

```
Time    Thread 1                        Thread 2
----    --------                        --------
0ms     READ balance = 100              READ balance = 100
1ms     CHECK 100 >= 30 → ok            CHECK 100 >= 30 → ok
2ms     SET balance = 70                SET balance = 70
3ms     COMMIT (balance = 70)           COMMIT (balance = 70)
Final:  balance = 70  ← wrong! Should be 40 (100 - 30 - 30)
```

Both threads read the same value (100), both passed the check, and both wrote the same
result (70). The first write was silently overwritten — a "lost update".

With `FOR UPDATE`:
```
Thread 1 acquires lock first, Thread 2 blocks at SELECT.
Thread 1: READ 100, deduct 30, COMMIT (balance = 70), lock released.
Thread 2 unblocks: READ 70, deduct 30, COMMIT (balance = 40).
Final: balance = 40 ← correct.
```
