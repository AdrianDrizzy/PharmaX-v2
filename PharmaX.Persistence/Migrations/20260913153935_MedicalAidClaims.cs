using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MedicalAidClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependantCode",
                table: "Patient",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MedicalAidSchemeId",
                table: "Patient",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MembershipNumber",
                table: "Patient",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicalAidClaim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SchemeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispensingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MembershipNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DependantCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ClaimedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WriteOffReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Claimed = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ClaimedCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Settled = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SettledCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    WrittenOff = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    WrittenOffCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalAidClaim", x => x.Id);
                });

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
