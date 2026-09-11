using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaX.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsOnHold = table.Column<bool>(type: "INTEGER", nullable: false),
                    HoldReason = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    TermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BalanceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LimitCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Workstation = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Batch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    CostPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeightKg = table.Column<decimal>(type: "TEXT", precision: 9, scale: 3, nullable: true),
                    IsPregnant = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBreastfeeding = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ChronicConditions = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Denomination",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    IsNote = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Denomination", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dispensing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RepeatNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    PharmacistId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispensing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberBlock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Series = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WorkstationCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RangeFrom = table.Column<long>(type: "INTEGER", nullable: false),
                    RangeTo = table.Column<long>(type: "INTEGER", nullable: false),
                    NextValue = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberBlock", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Surname = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FirstNames = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    IdNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClinicalProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patient", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    IsSupervisorEscalatable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GenericName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Schedule = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBatchTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TaxCode = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    ReorderLevel = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    PackSize = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DefaultSupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Barcodes = table.Column<string>(type: "TEXT", nullable: false),
                    CostPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    RetailPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetailCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repeat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepeatNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CollectedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DispensingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repeat", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    IsSystemRole = table.Column<bool>(type: "INTEGER", nullable: false),
                    PermissionCodes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sale", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Script",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrescriberName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    PrescriberNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    PrescribedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RepeatsAuthorised = table.Column<int>(type: "INTEGER", nullable: false),
                    RepeatsUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Script", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MovementType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SourceDocumentType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TillDeclaration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeclaredBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false),
                    VarianceReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillDeclaration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TillSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TillNumber = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpenedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosingDeclared = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ClosingVarianceReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    XReadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FloatCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillSession", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Credential = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLockedOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PasswordChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DiscountLimitPercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Roles = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Allergy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicalProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Substance = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reaction = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allergy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allergy_ClinicalProfile_ClinicalProfileId",
                        column: x => x.ClinicalProfileId,
                        principalTable: "ClinicalProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalWarning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispensingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispensingLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    OverrideReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalWarning", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalWarning_Dispensing_DispensingId",
                        column: x => x.DispensingId,
                        principalTable: "Dispensing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DispensingLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispensingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BatchExpiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Directions = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsSubstitution = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubstitutedForProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Contribution = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ContributionCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Tariff = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TariffCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispensingLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispensingLine_Dispensing_DispensingId",
                        column: x => x.DispensingId,
                        principalTable: "Dispensing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPrice_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchExpiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    TaxCode = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    PriceSource = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OverrideAuthoriserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsVoided = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UnitPriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLine_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalePayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tender = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalePayment_Sale_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScriptItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Directions = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AllowSubstitution = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptItem_Script_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "Script",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TillDeclarationCount",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    Extended = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TillDeclarationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillDeclarationCount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillDeclarationCount_TillDeclaration_TillDeclarationId",
                        column: x => x.TillDeclarationId,
                        principalTable: "TillDeclaration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TillDeclarationLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tender = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Expected = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Declared = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TillDeclarationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TillDeclarationLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TillDeclarationLine_TillDeclaration_TillDeclarationId",
                        column: x => x.TillDeclarationId,
                        principalTable: "TillDeclaration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_AccountNumber",
                table: "Account",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allergy_ClinicalProfileId",
                table: "Allergy",
                column: "ClinicalProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_EntityType_EntityId",
                table: "AuditEntry",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_OccurredAtUtc",
                table: "AuditEntry",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_Outcome_OccurredAtUtc",
                table: "AuditEntry",
                columns: new[] { "Outcome", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_UserId_OccurredAtUtc",
                table: "AuditEntry",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Batch_ProductId_BatchNumber",
                table: "Batch",
                columns: new[] { "ProductId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batch_ProductId_Status_ExpiryDate",
                table: "Batch",
                columns: new[] { "ProductId", "Status", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalProfile_PatientId",
                table: "ClinicalProfile",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalWarning_DispensingId",
                table: "ClinicalWarning",
                column: "DispensingId");

            migrationBuilder.CreateIndex(
                name: "IX_Denomination_Currency_Key",
                table: "Denomination",
                columns: new[] { "Currency", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispensing_PatientId_IssuedAtUtc",
                table: "Dispensing",
                columns: new[] { "PatientId", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Dispensing_ScriptId",
                table: "Dispensing",
                column: "ScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_DispensingLine_DispensingId_LineNo",
                table: "DispensingLine",
                columns: new[] { "DispensingId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberBlock_Series_WorkstationCode_RangeFrom",
                table: "DocumentNumberBlock",
                columns: new[] { "Series", "WorkstationCode", "RangeFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEntry_Status_CreatedAtUtc",
                table: "OutboxEntry",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Patient_FileNumber",
                table: "Patient",
                column: "FileNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patient_IdNumber",
                table: "Patient",
                column: "IdNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Patient_Surname_FirstNames",
                table: "Patient",
                columns: new[] { "Surname", "FirstNames" });

            migrationBuilder.CreateIndex(
                name: "IX_Permission_Code",
                table: "Permission",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_Code",
                table: "Product",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_Description",
                table: "Product",
                column: "Description");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrice_ProductId_Source",
                table: "ProductPrice",
                columns: new[] { "ProductId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_Repeat_DueOn",
                table: "Repeat",
                column: "DueOn");

            migrationBuilder.CreateIndex(
                name: "IX_Repeat_ScriptId_RepeatNumber",
                table: "Repeat",
                columns: new[] { "ScriptId", "RepeatNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Role_Name",
                table: "Role",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sale_BranchId_OccurredAtUtc",
                table: "Sale",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Sale_WorkstationId_DocumentNumber",
                table: "Sale",
                columns: new[] { "WorkstationId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleLine_ProductId",
                table: "SaleLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLine_SaleId_LineNo",
                table: "SaleLine",
                columns: new[] { "SaleId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalePayment_SaleId",
                table: "SalePayment",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Script_PatientId_PrescribedOn",
                table: "Script",
                columns: new[] { "PatientId", "PrescribedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Script_ScriptNumber",
                table: "Script",
                column: "ScriptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScriptItem_ScriptId_LineNo",
                table: "ScriptItem",
                columns: new[] { "ScriptId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_ProductId_OccurredAtUtc",
                table: "StockMovement",
                columns: new[] { "ProductId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_SourceDocumentType_SourceDocumentId",
                table: "StockMovement",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TillDeclaration_TillSessionId_DeclaredAtUtc",
                table: "TillDeclaration",
                columns: new[] { "TillSessionId", "DeclaredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TillDeclarationCount_TillDeclarationId",
                table: "TillDeclarationCount",
                column: "TillDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_TillDeclarationLine_TillDeclarationId",
                table: "TillDeclarationLine",
                column: "TillDeclarationId");

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_BranchId_OpenedAtUtc",
                table: "TillSession",
                columns: new[] { "BranchId", "OpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TillSession_WorkstationId_Status",
                table: "TillSession",
                columns: new[] { "WorkstationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_User_Username",
                table: "User",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Account");

            migrationBuilder.DropTable(
                name: "Allergy");

            migrationBuilder.DropTable(
                name: "AuditEntry");

            migrationBuilder.DropTable(
                name: "Batch");

            migrationBuilder.DropTable(
                name: "ClinicalWarning");

            migrationBuilder.DropTable(
                name: "Denomination");

            migrationBuilder.DropTable(
                name: "DispensingLine");

            migrationBuilder.DropTable(
                name: "DocumentNumberBlock");

            migrationBuilder.DropTable(
                name: "OutboxEntry");

            migrationBuilder.DropTable(
                name: "Patient");

            migrationBuilder.DropTable(
                name: "Permission");

            migrationBuilder.DropTable(
                name: "ProductPrice");

            migrationBuilder.DropTable(
                name: "Repeat");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropTable(
                name: "SaleLine");

            migrationBuilder.DropTable(
                name: "SalePayment");

            migrationBuilder.DropTable(
                name: "ScriptItem");

            migrationBuilder.DropTable(
                name: "StockMovement");

            migrationBuilder.DropTable(
                name: "TillDeclarationCount");

            migrationBuilder.DropTable(
                name: "TillDeclarationLine");

            migrationBuilder.DropTable(
                name: "TillSession");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "ClinicalProfile");

            migrationBuilder.DropTable(
                name: "Dispensing");

            migrationBuilder.DropTable(
                name: "Product");

            migrationBuilder.DropTable(
                name: "Sale");

            migrationBuilder.DropTable(
                name: "Script");

            migrationBuilder.DropTable(
                name: "TillDeclaration");
        }
    }
}
