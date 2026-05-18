using BankingLedger.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingLedger.Infrastructure.Configurations;

/// Maps the LedgerEntry domain entity to the "ledger_entries" PostgreSQL table.
/// Ledger entries are the heart of the audit trail — every balance change has a matching entry.
public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(l => l.AccountId)
            .HasColumnName("account_id")
            .HasColumnType("uuid")
            .IsRequired();

        // TransferId is nullable: deposit and withdrawal entries have no associated transfer.
        builder.Property(l => l.TransferId)
            .HasColumnName("transfer_id")
            .HasColumnType("uuid");

        builder.Property(l => l.Type)
            .HasColumnName("type")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(l => l.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(l => l.BalanceAfterTransaction)
            .HasColumnName("balance_after_transaction")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasColumnType("varchar(300)")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(l => l.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // Composite index: enables efficient "show ledger for account X in date order" queries.
        builder.HasIndex(l => new { l.AccountId, l.CreatedAtUtc })
            .HasDatabaseName("IX_ledger_entries_account_id_created_at_utc");

        builder.ToTable(t =>
        {
            // Every entry records a real amount — zero is meaningless, negative is impossible.
            t.HasCheckConstraint("CK_ledger_entries_amount_positive", "amount > 0");

            // Mirrors LedgerEntryType enum.
            t.HasCheckConstraint("CK_ledger_entries_type_valid",
                "type IN ('Deposit', 'Withdrawal', 'TransferDebit', 'TransferCredit')");
        });
    }
}
