using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <summary>
    /// The shared database's copy of RetireLegacyRecord: the old history
    /// table set aside under another name, not dropped.
    /// </summary>
    public partial class HostRetireLegacyRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "LegacyRecord",
                newName: "LegacyRecordArchive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "LegacyRecordArchive",
                newName: "LegacyRecord");
        }
    }
}
