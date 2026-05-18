# Transaction Design

## Why explicit transactions?

EF Core wraps each `SaveChangesAsync()` call in its own implicit transaction.
This works for single operations (one INSERT or UPDATE), but fails for multi-step operations.

A transfer requires six database changes. If we used separate `SaveChangesAsync()` calls
without an explicit transaction, a crash between calls would leave the database in an
inconsistent state — for example, the sender's balance reduced but the receiver's not yet increased.

Explicit transactions give us control over when all changes become atomic and durable together.

## Transaction pattern used throughout

```csharp
// Begin a transaction at the required isolation level.
await using var transaction = await db.Database.BeginTransactionAsync(
    IsolationLevel.ReadCommitted, cancellationToken);
try
{
    // Step 1: acquire row locks (FOR UPDATE)
    // Step 2: apply domain changes
    // Step 3: add new entities (ledger entries, transfer record)
    await db.SaveChangesAsync(cancellationToken);

    // All changes become durable simultaneously.
    await transaction.CommitAsync(cancellationToken);
}
catch
{
    // Any exception undoes all writes since BeginTransactionAsync.
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

## Which operations need explicit transactions?

| Operation | Needs explicit transaction? | Why |
|---|---|---|
| Create Account (with initial balance) | Yes | Two writes: Account + LedgerEntry |
| Deposit | Yes | Two writes: balance UPDATE + LedgerEntry INSERT |
| Withdraw | Yes | Two writes + FOR UPDATE lock |
| Transfer | Yes | Six writes: two balance UPDATEs + Transfer INSERT + two LedgerEntry INSERTs |
| Get Account | No | Read-only — no writes to protect |
| Get Ledger | No | Read-only |
| Get Transfers | No | Read-only |

## Why ledger entries must be in the same transaction

A ledger entry is proof that a balance change happened. If the balance changes in one
transaction and the ledger entry is written in a separate transaction, the system is
vulnerable to:

1. Crash between the two transactions → balance changed but no entry (invisible money movement)
2. The application reading a stale balance between transactions → incorrect state visible

By writing both the balance UPDATE and the LedgerEntry INSERT inside the same transaction,
we guarantee they either both commit or both roll back. The ledger is always accurate.

## Isolation Level Choices

### ReadCommitted (used in this project)

- Each statement sees data committed by other transactions at the moment the statement starts.
- Prevents dirty reads (reading uncommitted data from another transaction).
- Does NOT prevent lost updates — which is why we supplement with FOR UPDATE.

We chose ReadCommitted because:
1. It is PostgreSQL's default and works well for most OLTP workloads.
2. FOR UPDATE provides the additional isolation we need for concurrent modifications.
3. Higher levels (RepeatableRead, Serializable) would add overhead we don't need here.

See `docs/isolation-levels.md` for a full comparison.
