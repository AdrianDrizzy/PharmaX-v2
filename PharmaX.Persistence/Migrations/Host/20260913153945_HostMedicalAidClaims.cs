using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations.Host
{
    /// <inheritdoc />
    public partial class HostMedicalAidClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependantCode",
                table: "Patient",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "MedicalAidSchemeId",
                table: "Patient",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "MembershipNumber",
                table: "Patient",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MedicalAidClaim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClaimNumber = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SchemeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PatientId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DispensingId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MembershipNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DependantCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    WriteOffReason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Claimed = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClaimedCurrency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    Settled = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledCurrency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    WrittenOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WrittenOffCurrency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RowVersion = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalAidClaim", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAidClaim_ClaimNumber",
                table: "MedicalAidClaim",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAidClaim_DispensingId",
                table: "MedicalAidClaim",
                column: "DispensingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAidClaim_SchemeId_Status",
                table: "MedicalAidClaim",
                columns: new[] { "SchemeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAidClaim_Status_ClaimedOn",
                table: "MedicalAidClaim",
                columns: new[] { "Status", "ClaimedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicalAidClaim");

            migrationBuilder.DropColumn(
                name: "DependantCode",
                table: "Patient");

            migrationBuilder.DropColumn(
                name: "MedicalAidSchemeId",
                table: "Patient");

            migrationBuilder.DropColumn(
                name: "MembershipNumber",
                table: "Patient");
        }
    }
}
