using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <summary>
    /// The shared database's copy of ScriptItemPackPrice: a script line's
    /// own pack and price, null for the product's.
    /// </summary>
    public partial class HostScriptItemPackPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackPrice",
                table: "ScriptItem",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackUnits",
                table: "ScriptItem",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackPrice",
                table: "ScriptItem");

            migrationBuilder.DropColumn(
                name: "PackUnits",
                table: "ScriptItem");
        }
    }
}
