using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <summary>
    /// A pack and its price set on a script line for that script alone:
    /// the line is charged price x quantity / pack instead of the shelf
    /// price. Both null - every script captured before this - is the
    /// product's own, so nothing already on file changes.
    /// </summary>
    public partial class ScriptItemPackPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackPrice",
                table: "ScriptItem",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackUnits",
                table: "ScriptItem",
                type: "TEXT",
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
