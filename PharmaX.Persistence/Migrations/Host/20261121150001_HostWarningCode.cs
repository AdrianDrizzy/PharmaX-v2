using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host;

// shortcut: no model snapshot. The next host `dotnet ef migrations add`
// will try to create WarningCode again unless the snapshot is regenerated.
[DbContext(typeof(HostDbContext))]
[Migration("20261121150001_HostWarningCode")]
public class HostWarningCodeMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarningCode",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                RowVersion = table.Column<byte[]>(type: "longblob", nullable: false),
                Code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Detail = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarningCode", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

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
