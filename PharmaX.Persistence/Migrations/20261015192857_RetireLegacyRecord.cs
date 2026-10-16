using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <summary>
    /// The old system's history, as one JSON row per record, is no longer
    /// read: an import puts history into PharmaX's own tables now.
    /// </summary>
    /// <remarks>
    /// Set aside rather than dropped. A database that ran the earlier
    /// import holds a year of history here that nothing else holds, and
    /// the new import will not bring it in again once the pharmacy has
    /// traded. Renamed, nothing reads it and nothing is lost.
    /// </remarks>
    public partial class RetireLegacyRecord : Migration
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
