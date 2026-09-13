using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SettingsArePerWorkstation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkstationSetting",
                table: "WorkstationSetting");

            migrationBuilder.AddColumn<string>(
                name: "WorkstationCode",
                table: "WorkstationSetting",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkstationSetting",
                table: "WorkstationSetting",
                columns: new[] { "WorkstationCode", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkstationSetting",
                table: "WorkstationSetting");

            migrationBuilder.DropColumn(
                name: "WorkstationCode",
                table: "WorkstationSetting");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkstationSetting",
                table: "WorkstationSetting",
                column: "Key");
        }
    }
}
