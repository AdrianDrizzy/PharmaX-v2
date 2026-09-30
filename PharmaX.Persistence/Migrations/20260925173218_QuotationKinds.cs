using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuotationKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitsInPack",
                table: "QuotationLine",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Quotation",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "OverTheCounter");

            migrationBuilder.AddColumn<Guid>(
                name: "ScriptId",
                table: "Quotation",
                type: "TEXT",
                nullable: true);
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
