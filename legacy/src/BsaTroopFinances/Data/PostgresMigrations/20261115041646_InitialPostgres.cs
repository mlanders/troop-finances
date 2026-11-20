using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BsaTroopFinances.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminSignIns",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    UserKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Provider = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    VerifiedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSignIns", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "AdminUsers",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Role = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IsCommitteeChair = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RoleChangedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RoleChangedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanTurnOnEdit = table.Column<bool>(type: "boolean", nullable: false),
                    CanTurnOnEditUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EditUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EditReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsGlobalAdmin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepositLines",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Kind = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Payer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Method = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FundraiserId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositLines", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Deposits",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    AccountId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CheckTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CashTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    OtherTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CashCount = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CountedBy1 = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CountedBy2 = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    TransactionRowKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deposits", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Donations",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    DonorName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DonorEmail = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DonorAddress = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IsRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AcknowledgedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Donations", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "EmailLog",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    EmailType = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ToEmail = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SentUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RelatedEntityId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLog", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "EmailSettings",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    ReceiptSubmittedEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReceiptStatusEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InvoiceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EditorGrantedEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AdminRecipients = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettings", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "FinanceAudit",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Action = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Actor = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAudit", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "FundraiserEvents",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    EventDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TotalRaised = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalExpenses = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    GoalAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StatusValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundraiserEvents", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "GuardianAuthenticators",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    SecretProtected = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnabledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedStep = table.Column<long>(type: "bigint", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianAuthenticators", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Guardians",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    GuardianId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    FullName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guardians", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "GuardianScoutLinks",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    GuardianId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ScoutId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ScoutName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianScoutLinks", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    InvoiceId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ScoutId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PaidUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaymentGroupId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    GroupTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    ScoutId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ScoutName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    AmountAppliedFromAccount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    AmountDueFromFamily = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    GuardianId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    GuardianName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    StatusValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PaidUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    VoidedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LedgerEntryRowKey = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    InvoiceId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "ListViews",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NameSearch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DescriptionSearch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListViews", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "LoginCodes",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginCodes", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Reconciliations",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    StatementDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Cleared = table.Column<string>(type: "text", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliations", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "ReimbursementRequests",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    SubmitterGuardianId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SubmitterName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SubmitterEmail = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SubmittedOnBehalfBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ScoutId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ScoutName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    AmountRequested = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReceiptBlobPath = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptContentType = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptOriginalFileName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StatusValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SubmittedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PaidUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PaymentMethodNote = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementRequests", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "ScoutLedger",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    EntryTypeValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceTypeValue = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutLedger", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Scouts",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    FullName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Rank = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PatrolName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CachedBalance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    BalanceUpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scouts", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "TroopAccounts",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Kind = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    OpeningDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TroopAccounts", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "TroopTransactions",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: true),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Payee = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptBlobPath = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptContentType = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReceiptFileName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ReconciliationId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TroopTransactions", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminSignIns_UserKey",
                table: "AdminSignIns",
                column: "UserKey");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSignIns_VerifiedEmail",
                table: "AdminSignIns",
                column: "VerifiedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Role",
                table: "AdminUsers",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_DepositLines_Kind_SourceId",
                table: "DepositLines",
                columns: new[] { "Kind", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_Date",
                table: "Deposits",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Donations_ReceivedDate",
                table: "Donations",
                column: "ReceivedDate");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLog_SentUtc",
                table: "EmailLog",
                column: "SentUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_EntityType_EntityId",
                table: "FinanceAudit",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAudit_OccurredUtc",
                table: "FinanceAudit",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FundraiserEvents_EventDate",
                table: "FundraiserEvents",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_Guardians_GuardianId",
                table: "Guardians",
                column: "GuardianId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianScoutLinks_ScoutId",
                table: "GuardianScoutLinks",
                column: "ScoutId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_PaidUtc",
                table: "InvoicePayments",
                column: "PaidUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GuardianId",
                table: "Invoices",
                column: "GuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceId",
                table: "Invoices",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedUtc",
                table: "Invoices",
                column: "IssuedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ScoutId",
                table: "Invoices",
                column: "ScoutId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StatusValue_IssuedUtc",
                table: "Invoices",
                columns: new[] { "StatusValue", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_PaidUtc",
                table: "ReimbursementRequests",
                column: "PaidUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_RequestId",
                table: "ReimbursementRequests",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_ScoutId",
                table: "ReimbursementRequests",
                column: "ScoutId");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_StatusValue_SubmittedUtc",
                table: "ReimbursementRequests",
                columns: new[] { "StatusValue", "SubmittedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_SubmittedUtc",
                table: "ReimbursementRequests",
                column: "SubmittedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutLedger_CreatedUtc",
                table: "ScoutLedger",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScoutLedger_SourceTypeValue_SourceId",
                table: "ScoutLedger",
                columns: new[] { "SourceTypeValue", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Scouts_IsActive",
                table: "Scouts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TroopTransactions_ReconciliationId",
                table: "TroopTransactions",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_TroopTransactions_SourceType_SourceId",
                table: "TroopTransactions",
                columns: new[] { "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminSignIns");

            migrationBuilder.DropTable(
                name: "AdminUsers");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "DepositLines");

            migrationBuilder.DropTable(
                name: "Deposits");

            migrationBuilder.DropTable(
                name: "Donations");

            migrationBuilder.DropTable(
                name: "EmailLog");

            migrationBuilder.DropTable(
                name: "EmailSettings");

            migrationBuilder.DropTable(
                name: "FinanceAudit");

            migrationBuilder.DropTable(
                name: "FundraiserEvents");

            migrationBuilder.DropTable(
                name: "GuardianAuthenticators");

            migrationBuilder.DropTable(
                name: "Guardians");

            migrationBuilder.DropTable(
                name: "GuardianScoutLinks");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "ListViews");

            migrationBuilder.DropTable(
                name: "LoginCodes");

            migrationBuilder.DropTable(
                name: "Reconciliations");

            migrationBuilder.DropTable(
                name: "ReimbursementRequests");

            migrationBuilder.DropTable(
                name: "ScoutLedger");

            migrationBuilder.DropTable(
                name: "Scouts");

            migrationBuilder.DropTable(
                name: "TroopAccounts");

            migrationBuilder.DropTable(
                name: "TroopTransactions");
        }
    }
}
