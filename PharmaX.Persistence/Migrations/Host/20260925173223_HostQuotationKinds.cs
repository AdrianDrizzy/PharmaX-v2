using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <inheritdoc />
    public partial class HostQuotationKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitsInPack",
                table: "QuotationLine",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Quotation",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "OverTheCounter")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ScriptId",
                table: "Quotation",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitsInPack",
                table: "QuotationLine");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "ScriptId",
                table: "Quotation");
        }
    }
}
