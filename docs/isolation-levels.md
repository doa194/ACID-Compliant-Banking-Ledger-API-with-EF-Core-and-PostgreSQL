# Isolation Levels

Isolation levels control how much a transaction is exposed to changes made by other
concurrent transactions. Higher isolation = fewer anomalies, but more contention and
potentially lower throughput.

## The four isolation levels (SQL standard)

### 1. Read Uncommitted

- A transaction can read rows that another transaction has modified but not yet committed.
- Problem: **dirty reads** — you might read data that gets rolled back and never existed.
- **Not supported by PostgreSQL** — PostgreSQL upgrades Read Uncommitted to Read Committed.

### 2. Read Committed ← Used in this project

- A statement only sees data that was committed before the statement started.
- Prevents dirty reads.
- **Does NOT prevent**: lost updates (two transactions both read then write same row).

**Example — demonstrating Read Committed:**
```
T1: BEGIN
T1: SELECT balance FROM accounts WHERE id = @id  → reads 100

T2: BEGIN
T2: UPDATE accounts SET balance = 200 WHERE id = @id
T2: COMMIT

T1: SELECT balance FROM accounts WHERE id = @id  → reads 200 (sees T2's committed change)
T1: COMMIT
```
T1's second SELECT sees the new value because T2 committed before the statement started.

**In this project**: we use Read Committed for all transactions and supplement with
`FOR UPDATE` row locking to prevent lost updates on balance-modifying operations.

### 3. Repeatable Read

- All reads within a transaction see the same snapshot as the transaction's first read.
- Prevents dirty reads AND non-repeatable reads.
- PostgreSQL also prevents phantom reads at this level (unlike the SQL standard).

**Example — demonstrating Repeatable Read:**
```
T1: BEGIN (REPEATABLE READ)
T1: SELECT balance FROM accounts WHERE id = @id  → reads 100

T2: BEGIN
T2: UPDATE accounts SET balance = 200 WHERE id = @id
T2: COMMIT

T1: SELECT balance FROM accounts WHERE id = @id  → still reads 100 (snapshot from T1's start)
T1: COMMIT
```
T1 sees a consistent snapshot throughout its lifetime.

**When to use**: reporting queries that must see consistent data across multiple reads.

### 4. Serializable

- Transactions execute as if they were serialised one after another (no overlap).
- PostgreSQL uses Serializable Snapshot Isolation (SSI) to detect read-write conflicts.
- When a conflict is detected, one of the conflicting transactions gets a serialization error
  and must retry.

**Example — demonstrating why Serializable is sometimes needed:**
```
Account A has 100, Account B has 100. Business rule: total across all accounts >= 0.

T1: Reads A=100, decides to subtract 100 from A
T2: Reads B=100, decides to subtract 100 from B

Without Serializable:
  T1 commits: A = 0
  T2 commits: B = 0
  Total = 0, OK

But what if the rule were "each individual account must not go below -50"?
T1 reads A=100 and B=100, decides A can go to 0 (fine)
T2 reads A=100 and B=100, decides B can go to 0 (fine)
After both commit: A=0, B=0 — constraint satisfied.

But if T1 reads A=-50 and concludes it cannot transfer, then T2 commits changing B,
and then T1 is re-evaluated with the new reality — Serializable prevents this class
of read-write conflict by aborting one transaction.
```

**When to use**: complex multi-row invariants where the decision to write depends on what
was read. Requires retry logic on `SerializationFailure` errors.

## Choosing an isolation level

| Scenario | Recommended Level | Why |
|---|---|---|
| Simple deposit/withdrawal | ReadCommitted + FOR UPDATE | Sufficient isolation, minimal overhead |
| Transfer between accounts | ReadCommitted + ordered FOR UPDATE | Prevents lost updates, avoids deadlocks |
| Reporting/audit queries | RepeatableRead | Consistent snapshot across multiple reads |
| Complex invariants across rows | Serializable | Full isolation but requires retry logic |

## How PostgreSQL implements isolation

PostgreSQL uses **Multi-Version Concurrency Control (MVCC)**. Each write creates a new
version of the row rather than overwriting the old one. Readers see the version that was
current at their transaction's start time. This means readers never block writers and
writers never block readers — only writers that touch the same row block each other
(via row locks).

`xmin` (the concurrency token we use) is part of MVCC: it identifies which transaction
wrote the current version of a row.
