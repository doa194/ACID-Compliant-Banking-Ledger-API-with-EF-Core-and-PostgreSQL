using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── accounts table ────────────────────────────────────────────────────────────────────
            // Stores all bank accounts.
            // NOTE: "xmin" is NOT listed here — it is a PostgreSQL system column that every
            // table already has automatically.  PostgreSQL updates it with the transaction ID
            // on every write, which is exactly what we use for optimistic concurrency detection.
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);

                    // Consistency: the database enforces non-negative balance as a safety net.
                    // Even if application code has a bug, this constraint blocks the write.
                    table.CheckConstraint("CK_accounts_balance_non_negative", "balance >= 0");

                    // Consistency: owner name must be a non-empty string.
                    table.CheckConstraint("CK_accounts_owner_name_not_empty", "owner_name <> ''");

                    // Consistency: only the three valid lifecycle states are accepted.
                    table.CheckConstraint("CK_accounts_status_valid",
                        "status IN ('Active', 'Closed', 'Frozen')");
                });

            // ── transfers table ───────────────────────────────────────────────────────────────────
            // Records every money movement request and its final outcome.
            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.id);
                    table.CheckConstraint("CK_transfers_amount_positive", "amount > 0");
                    table.CheckConstraint("CK_transfers_different_accounts",
                        "from_account_id <> to_account_id");
                    table.CheckConstraint("CK_transfers_status_valid",
                        "status IN ('Pending', 'Completed', 'Failed', 'RolledBack')");
                });

            // ── ledger_entries table ──────────────────────────────────────────────────────────────
            // Immutable audit trail — every balance change is recorded here.
            // Durability: once a row exists here, it proves the transaction committed.
            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    balance_after_transaction = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("CK_ledger_entries_amount_positive", "amount > 0");
                    table.CheckConstraint("CK_ledger_entries_type_valid",
                        "type IN ('Deposit', 'Withdrawal', 'TransferDebit', 'TransferCredit')");
                });

            // ── Indexes ──────────────────────────────────────────────────────────────────────────
            // These make the common read patterns fast — filtering by account and sorting by time.

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_account_id_created_at_utc",
                table: "ledger_entries",
                columns: new[] { "account_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_from_account_id",
                table: "transfers",
                column: "from_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_to_account_id",
                table: "transfers",
                column: "to_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_created_at_utc",
                table: "transfers",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse dependency order: entries depend on transfers and accounts.
            migrationBuilder.DropTable(name: "ledger_entries");
            migrationBuilder.DropTable(name: "transfers");
            migrationBuilder.DropTable(name: "accounts");
        }
    }
}
