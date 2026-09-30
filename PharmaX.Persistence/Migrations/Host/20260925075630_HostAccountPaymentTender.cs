using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <inheritdoc />
    public partial class HostAccountPaymentTender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tender",
                table: "AccountTransaction",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "TillSessionId",
                table: "AccountTransaction",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_TillSessionId",
                table: "AccountTransaction",
                column: "TillSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountTransaction_TillSessionId",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "Tender",
                table: "AccountTransaction");

            migrationBuilder.DropColumn(
                name: "TillSessionId",
                table: "AccountTransaction");
        }
    }
}
