using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <summary>
    /// Money taken out of a till's drawer during its shift - drawings,
    /// lunch, other expenses - with the amount and what it was for, so the
    /// cash-up expects that much less. A new table; nothing already on file
    /// changes.
    /// </summary>
    public partial class DrawerPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TillPayout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillPayout", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TillPayout_TillSessionId_PaidAtUtc",
                table: "TillPayout",
                columns: new[] { "TillSessionId", "PaidAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TillPayout");
        }
    }
}
