using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispensingBatchesTillAndNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeCurrency",
                table: "Sale",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeGiven",
                table: "Sale",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationId",
                table: "Sale",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "Quotation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitsPerPack",
                table: "Product",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthorisedBy",
                table: "ClinicalWarning",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DispensingLineBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispensingLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispensingLineBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispensingLineBatch_DispensingLine_DispensingLineId",
                        column: x => x.DispensingLineId,
                        principalTable: "DispensingLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sale_QuotationId",
                table: "Sale",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_Sale_Status_OccurredAtUtc",
                table: "Sale",
                columns: new[] { "Status", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DispensingLineBatch_BatchId",
                table: "DispensingLineBatch",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DispensingLineBatch_DispensingLineId",
                table: "DispensingLineBatch",
                column: "DispensingLineId");

            // Sales finalised before change was recorded carry no change
            // currency; give them the currency they were paid in, so that
            // tendered (payments plus change) can be added up.
            migrationBuilder.Sql(
                "UPDATE \"Sale\" SET \"ChangeCurrency\" = COALESCE("
                + "(SELECT \"Currency\" FROM \"SalePayment\" "
                + "WHERE \"SalePayment\".\"SaleId\" = \"Sale\".\"Id\" LIMIT 1), "
                + "(SELECT \"UnitPriceCurrency\" FROM \"SaleLine\" "
                + "WHERE \"SaleLine\".\"SaleId\" = \"Sale\".\"Id\" LIMIT 1), "
                + "'USD') WHERE \"ChangeCurrency\" = '';");

            // DEF-0010: scripts, stocktakes and purchase orders were
            // numbered with the same one-letter prefix as sales and
            // patient files. Existing ones move to their own prefix, so no
            // two documents of different kinds share a number.
            migrationBuilder.Sql(
                "UPDATE \"Script\" SET \"ScriptNumber\" = 'SC' || "
                + "substr(\"ScriptNumber\", 2) WHERE \"ScriptNumber\" LIKE 'S-%';");

            migrationBuilder.Sql(
                "UPDATE \"Stocktake\" SET \"StocktakeNumber\" = 'ST' || "
                + "substr(\"StocktakeNumber\", 2) WHERE \"StocktakeNumber\" LIKE 'S-%';");

            migrationBuilder.Sql(
                "UPDATE \"PurchaseOrder\" SET \"OrderNumber\" = 'PO' || "
                + "substr(\"OrderNumber\", 2) WHERE \"OrderNumber\" LIKE 'P-%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispensingLineBatch");

            migrationBuilder.DropIndex(
                name: "IX_Sale_QuotationId",
                table: "Sale");

            migrationBuilder.DropIndex(
                name: "IX_Sale_Status_OccurredAtUtc",
                table: "Sale");

            migrationBuilder.DropColumn(
                name: "ChangeCurrency",
                table: "Sale");

            migrationBuilder.DropColumn(
                name: "ChangeGiven",
                table: "Sale");

            migrationBuilder.DropColumn(
                name: "QuotationId",
                table: "Sale");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "UnitsPerPack",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "AuthorisedBy",
                table: "ClinicalWarning");
        }
    }
}
