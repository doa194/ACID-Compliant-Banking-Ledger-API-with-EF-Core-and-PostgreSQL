using BankingLedger.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingLedger.Infrastructure.Configurations;

/// Maps the Transfer domain entity to the "transfers" PostgreSQL table.
/// Enforces database-level constraints that maintain Consistency even if the application
/// layer were to have a bug.
public sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfers");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(t => t.FromAccountId)
            .HasColumnName("from_account_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.ToAccountId)
            .HasColumnName("to_account_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(t => t.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(t => t.FailureReason)
            .HasColumnName("failure_reason")
            .HasColumnType("text");

        // Indexes speed up the common queries: "show transfers from/to this account" and
        // "show all transfers ordered by time".
        builder.HasIndex(t => t.FromAccountId).HasDatabaseName("IX_transfers_from_account_id");
        builder.HasIndex(t => t.ToAccountId).HasDatabaseName("IX_transfers_to_account_id");
        builder.HasIndex(t => t.CreatedAtUtc).HasDatabaseName("IX_transfers_created_at_utc");

        builder.ToTable(t =>
        {
            // Transfers must move a positive amount — zero-value or negative transfers make no sense.
            t.HasCheckConstraint("CK_transfers_amount_positive", "amount > 0");

            // An account cannot transfer money to itself.
            t.HasCheckConstraint("CK_transfers_different_accounts",
                "from_account_id <> to_account_id");

            // Mirrors TransferStatus enum — only known values are valid in the database.
            t.HasCheckConstraint("CK_transfers_status_valid",
                "status IN ('Pending', 'Completed', 'Failed', 'RolledBack')");
        });
    }
}
