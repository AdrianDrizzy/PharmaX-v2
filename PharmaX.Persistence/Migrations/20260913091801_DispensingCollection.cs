using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispensingCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DispensingId",
                table: "Sale",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CollectedAtUtc",
                table: "Dispensing",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "Dispensing",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sale_DispensingId",
                table: "Sale",
                column: "DispensingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispensing_Status_SaleId_IssuedAtUtc",
                table: "Dispensing",
                columns: new[] { "Status", "SaleId", "IssuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sale_DispensingId",
                table: "Sale");

            migrationBuilder.DropIndex(
                name: "IX_Dispensing_Status_SaleId_IssuedAtUtc",
                table: "Dispensing");

            migrationBuilder.DropColumn(
                name: "DispensingId",
                table: "Sale");

            migrationBuilder.DropColumn(
                name: "CollectedAtUtc",
                table: "Dispensing");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "Dispensing");
        }
    }
}
