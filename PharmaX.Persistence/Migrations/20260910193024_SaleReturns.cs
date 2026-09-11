using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SaleId",
                table: "SalePayment",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SaleReturnId",
                table: "SalePayment",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SaleReturn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OriginalSaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalDocumentNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorisedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturn", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturnLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleReturnId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalLineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalSaleLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchExpiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    TaxCode = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    RestockToShelf = table.Column<bool>(type: "INTEGER", nullable: false),
                    LineReason = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturnLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleReturnLine_SaleReturn_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalePayment_SaleReturnId",
                table: "SalePayment",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturn_DocumentNumber",
                table: "SaleReturn",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturn_OriginalSaleId",
                table: "SaleReturn",
                column: "OriginalSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturn_TillSessionId_OccurredAtUtc",
                table: "SaleReturn",
                columns: new[] { "TillSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLine_OriginalSaleLineId",
                table: "SaleReturnLine",
                column: "OriginalSaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLine_SaleReturnId_LineNo",
                table: "SaleReturnLine",
                columns: new[] { "SaleReturnId", "LineNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SalePayment_SaleReturn_SaleReturnId",
                table: "SalePayment",
                column: "SaleReturnId",
                principalTable: "SaleReturn",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalePayment_SaleReturn_SaleReturnId",
                table: "SalePayment");

            migrationBuilder.DropTable(
                name: "SaleReturnLine");

            migrationBuilder.DropTable(
                name: "SaleReturn");

            migrationBuilder.DropIndex(
                name: "IX_SalePayment_SaleReturnId",
                table: "SalePayment");

            migrationBuilder.DropColumn(
                name: "SaleReturnId",
                table: "SalePayment");

            migrationBuilder.AlterColumn<Guid>(
                name: "SaleId",
                table: "SalePayment",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
