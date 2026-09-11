using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClearDraftDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A migration of its own rather than a step inside the one
            // that widened the column: EF rebuilds a SQLite table to
            // alter it, and the rebuild is emitted after the raw SQL in
            // the same migration, so this ran against the old NOT NULL
            // column and failed.
            migrationBuilder.Sql(
                "UPDATE \"Sale\" SET \"DocumentNumber\" = NULL "
                + "WHERE \"DocumentNumber\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Sale\" SET \"DocumentNumber\" = '' "
                + "WHERE \"DocumentNumber\" IS NULL;");
        }
    }
}
