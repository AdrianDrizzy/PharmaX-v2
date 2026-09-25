using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <inheritdoc />
    public partial class HostDispensingBatchesTillAndNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeCurrency",
                table: "Sale",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeGiven",
                table: "Sale",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationId",
                table: "Sale",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "Quotation",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitsPerPack",
                table: "Product",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthorisedBy",
                table: "ClinicalWarning",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "DispensingLineBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DispensingLineId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BatchNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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

            // See the workstation migration of the same name.
            migrationBuilder.Sql(
                "UPDATE `Sale` SET `ChangeCurrency` = COALESCE("
                + "(SELECT sp.`Currency` FROM `SalePayment` sp "
                + "WHERE sp.`SaleId` = `Sale`.`Id` LIMIT 1), "
                + "(SELECT sl.`UnitPriceCurrency` FROM `SaleLine` sl "
                + "WHERE sl.`SaleId` = `Sale`.`Id` LIMIT 1), "
                + "'USD') WHERE `ChangeCurrency` = '';");

            migrationBuilder.Sql(
                "UPDATE `Script` SET `ScriptNumber` = CONCAT('SC', "
                + "SUBSTRING(`ScriptNumber`, 2)) WHERE `ScriptNumber` LIKE 'S-%';");

            migrationBuilder.Sql(
                "UPDATE `Stocktake` SET `StocktakeNumber` = CONCAT('ST', "
                + "SUBSTRING(`StocktakeNumber`, 2)) WHERE `StocktakeNumber` LIKE 'S-%';");

            migrationBuilder.Sql(
                "UPDATE `PurchaseOrder` SET `OrderNumber` = CONCAT('PO', "
                + "SUBSTRING(`OrderNumber`, 2)) WHERE `OrderNumber` LIKE 'P-%';");
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
