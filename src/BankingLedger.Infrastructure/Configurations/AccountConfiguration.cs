using BankingLedger.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingLedger.Infrastructure.Configurations;

/// Maps the Account domain entity to the "accounts" PostgreSQL table.
/// Also defines database-level constraints that enforce Consistency as a second line of
/// defence (the first line is the domain entity's validation methods).
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(a => a.OwnerName)
            .HasColumnName("owner_name")
            .HasColumnType("varchar(150)")
            .HasMaxLength(150)
            .IsRequired();

        // numeric(18,2): stores up to 18 digits with 2 decimal places — appropriate for currency.
        // "decimal" in C# maps to this by default with EF Core + Npgsql.
        builder.Property(a => a.Balance)
            .HasColumnName("balance")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // Storing enum as string makes the DB self-documenting and migration-friendly.
        // A stored integer (e.g. 0, 1, 2) would require looking up the enum to understand it.
        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(a => a.ClosedAtUtc)
            .HasColumnName("closed_at_utc");

        // Ignore the public Version property on the C# entity — it is for documentation
        // and logging purposes only.  The actual xmin concurrency token is a shadow property
        // managed entirely by Npgsql via UseXminAsConcurrencyToken() below.
        builder.Ignore(a => a.Version);

        // ISOLATION — Optimistic Concurrency via PostgreSQL xmin:
        //
        // Every PostgreSQL row has a hidden system column called "xmin" that stores the
        // transaction ID of the last write.  PostgreSQL increments this automatically on
        // every UPDATE — we never set it ourselves.
        //
        // Configure xmin as a shadow concurrency token.
        // This is equivalent to calling builder.UseXminAsConcurrencyToken() from Npgsql.
        //
        // IsConcurrencyToken() tells EF Core to:
        //   1. Include "xmin" in every SELECT query so the value is remembered.
        //   2. Add "AND xmin = @original_xmin" to every UPDATE statement.
        //   3. If no rows are updated (because another transaction already changed xmin),
        //      throw DbUpdateConcurrencyException.
        //
        // ValueGeneratedOnAddOrUpdate() tells EF Core + Npgsql that PostgreSQL manages
        // this value — do NOT include it in INSERT/UPDATE statements or DDL CREATE TABLE.
        //
        // This means two concurrent transactions both trying to update the same account
        // will NOT silently overwrite each other — one will succeed and the other will
        // get a DbUpdateConcurrencyException, which the service layer catches and retries.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // DATABASE-LEVEL CONSTRAINTS — Consistency's second line of defence:
        // Even if application bugs bypass domain validation, the database refuses invalid data.

        builder.ToTable(t =>
        {
            // balance >= 0: no account can ever have a negative balance in the database.
            t.HasCheckConstraint("CK_accounts_balance_non_negative", "balance >= 0");

            // owner_name <> '': the owner's name cannot be an empty string.
            t.HasCheckConstraint("CK_accounts_owner_name_not_empty", "owner_name <> ''");

            // Only the three enum values are valid in the database — mirrors AccountStatus.
            t.HasCheckConstraint("CK_accounts_status_valid",
                "status IN ('Active', 'Closed', 'Frozen')");
        });
    }
}
