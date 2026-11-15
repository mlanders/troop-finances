using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailLog",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    EmailType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ToEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SentUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RelatedEntityId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLog", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "EmailSettings",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ReceiptSubmittedEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReceiptStatusEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InvoiceEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AdminRecipients = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettings", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "FinanceAudit",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAudit", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "FundraiserEvents",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EventDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TotalRaised = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalExpenses = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    GoalAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    StatusValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundraiserEvents", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Guardians",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    GuardianId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guardians", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "GuardianScoutLinks",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    GuardianId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ScoutId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ScoutName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianScoutLinks", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    InvoiceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ScoutId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PaidUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaymentGroupId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    GroupTotal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ScoutId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ScoutName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    AmountAppliedFromAccount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    AmountDueFromFamily = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    StatusValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PaidUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    VoidedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LedgerEntryRowKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InvoiceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "LoginCodes",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginCodes", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "ReimbursementRequests",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SubmitterGuardianId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubmitterName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubmitterEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubmittedOnBehalfBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ScoutId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ScoutName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AmountRequested = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ReceiptBlobPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReceiptContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReceiptOriginalFileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReceiptSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StatusValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubmittedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PaidUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PaymentMethodNote = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementRequests", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "ScoutLedger",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    EntryTypeValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SourceTypeValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    HoursWorked = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoutLedger", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "Scouts",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Rank = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PatrolName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CachedBalance = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    BalanceUpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scouts", x => new { x.PartitionKey, x.RowKey });
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailLog");

            migrationBuilder.DropTable(
                name: "EmailSettings");

            migrationBuilder.DropTable(
                name: "FinanceAudit");

            migrationBuilder.DropTable(
                name: "FundraiserEvents");

            migrationBuilder.DropTable(
                name: "Guardians");

            migrationBuilder.DropTable(
                name: "GuardianScoutLinks");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "LoginCodes");

            migrationBuilder.DropTable(
                name: "ReimbursementRequests");

            migrationBuilder.DropTable(
                name: "ScoutLedger");

            migrationBuilder.DropTable(
                name: "Scouts");
        }
    }
}
