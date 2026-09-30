using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountPaymentTender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tender",
                table: "AccountTransaction",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TillSessionId",
                table: "AccountTransaction",
                type: "TEXT",
                nullable: true);

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
