using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <inheritdoc />
    public partial class HostScriptSaleLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScript",
                table: "SaleLine",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Every collection so far held only the script's lines.
            migrationBuilder.Sql("UPDATE SaleLine l JOIN Sale s ON s.Id = l.SaleId SET l.IsScript = 1 WHERE s.DispensingId IS NOT NULL;");
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
