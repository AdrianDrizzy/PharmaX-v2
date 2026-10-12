using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScriptSaleLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScript",
                table: "SaleLine",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Every collection so far held only the script's lines.
            migrationBuilder.Sql("UPDATE SaleLine SET IsScript = 1 WHERE SaleId IN (SELECT Id FROM Sale WHERE DispensingId IS NOT NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsScript",
                table: "SaleLine");
        }
    }
}
