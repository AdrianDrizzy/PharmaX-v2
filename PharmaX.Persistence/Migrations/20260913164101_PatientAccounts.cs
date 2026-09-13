using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTransaction", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_AccountId_OccurredOn",
                table: "AccountTransaction",
                columns: new[] { "AccountId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTransaction");
        }
    }
}
