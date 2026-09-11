using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stocktake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stocktake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StocktakeNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    VarianceReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktake", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BatchExpiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpectedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeLine_Stocktake_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktake",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktake_BranchId_Status",
                table: "Stocktake",
                columns: new[] { "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktake_StocktakeNumber",
                table: "Stocktake",
                column: "StocktakeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_ProductId",
                table: "StocktakeLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLine_StocktakeId_LineNo",
                table: "StocktakeLine",
                columns: new[] { "StocktakeId", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StocktakeLine");

            migrationBuilder.DropTable(
                name: "Stocktake");
        }
    }
}
