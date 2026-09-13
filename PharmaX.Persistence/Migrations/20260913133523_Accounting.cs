using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Accounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsPosted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalBatch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccount", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsDebit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLine_JournalBatch_JournalBatchId",
                        column: x => x.JournalBatchId,
                        principalTable: "JournalBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatch_BatchNumber",
                table: "JournalBatch",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatch_PostingDate",
                table: "JournalBatch",
                column: "PostingDate");

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatch_Source_PostingDate",
                table: "JournalBatch",
                columns: new[] { "Source", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_AccountCode",
                table: "JournalLine",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_JournalBatchId",
                table: "JournalLine",
                column: "JournalBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccount_Code",
                table: "LedgerAccount",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalLine");

            migrationBuilder.DropTable(
                name: "LedgerAccount");

            migrationBuilder.DropTable(
                name: "JournalBatch");
        }
    }
}
