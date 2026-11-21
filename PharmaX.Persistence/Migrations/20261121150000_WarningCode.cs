using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations;

// shortcut: no model snapshot. The next `dotnet ef migrations add` will
// try to create WarningCode again unless the snapshot is regenerated first.
[DbContext(typeof(PharmaXDbContext))]
[Migration("20261121150000_WarningCode")]
public class WarningCodeMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarningCode",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Detail = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarningCode", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarningCode_Code",
            table: "WarningCode",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarningCode");
    }
}
