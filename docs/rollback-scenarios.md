# Rollback Scenarios

A rollback restores the database to the exact state it was in before the transaction started.
This is the "A" in ACID — Atomicity.

## Scenario 1: Transfer fails after sender debit

This is the most important rollback scenario in the system.

### What happens without a transaction

```
Step 1: Sender balance: 1000 → 800  ✓ saved
Step 2: [network failure or bug]
Step 3: Receiver balance: never updated

Final state: Sender has 800, Receiver still has 500.
200 has disappeared from the system. This is a critical data corruption.
```

### What happens with a transaction

```
BEGIN TRANSACTION
  Step 1: Sender balance: 1000 → 800  (written, not committed)
  Step 2: [network failure or bug throws an exception]
ROLLBACK
  → Sender balance reverts to 1000
  → Database is in exactly the state it was before BEGIN TRANSACTION

Final state: Sender still has 1000, Receiver still has 500. No money lost.
```

### Demo endpoint

`POST /api/demo/atomicity/failed-transfer`

### Test

`RollbackScenarioTests.ForcedRollback_BetweenDebitAndCredit_LeavesNeitherBalanceChanged`

---

## Scenario 2: Domain exception before any write

If `InsufficientFundsException` is thrown inside a domain method, we call RollbackAsync
even though no SaveChangesAsync was called yet. This is a belt-and-suspenders approach —
the rollback is a no-op in this case but ensures the transaction is always properly closed.

```
BEGIN TRANSACTION
  SELECT ... FOR UPDATE  (lock acquired)
  account.Withdraw(9999)  → throws InsufficientFundsException
ROLLBACK
  → Lock is released, no writes occurred
```

---

## Scenario 3: Database constraint violation

If application code were to bypass domain validation and try to write a negative balance,
PostgreSQL's CHECK constraint would reject it with an error. EF Core translates this into
a `DbUpdateException`. Our exception middleware catches this and returns an HTTP 500.

The transaction is automatically rolled back when an exception occurs.

```
BEGIN TRANSACTION
  UPDATE accounts SET balance = -50 WHERE id = ...
  → PostgreSQL rejects: violates check constraint "CK_accounts_balance_non_negative"
ROLLBACK (automatic)
```

---

## What rollback does NOT undo

- Changes committed in a **previous** transaction (before the current transaction started).
- Side effects outside the database (e.g., an email sent, a REST call made).

This is why we do not send notifications, emails, or external API calls inside a transaction.
External calls should happen AFTER the commit succeeds, and they must be idempotent.

---

## Database state after rollback

After a rollback, the database is guaranteed to be in the state it was in just before
`BeginTransactionAsync()` was called. This includes:

- All balance values restored
- All inserted rows (LedgerEntries, Transfers) removed
- All locks released
- The WAL entries for the rolled-back transaction marked as aborted

Other concurrent transactions that were waiting for a lock held by the rolled-back transaction
will now proceed with the restored (pre-transaction) data.
