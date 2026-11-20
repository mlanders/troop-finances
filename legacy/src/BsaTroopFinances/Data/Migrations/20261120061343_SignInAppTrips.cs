using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class SignInAppTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignInPeople",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PersonType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsActiveInSignIn = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LinkedScoutId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LinkedGuardianId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NotBilled = table.Column<bool>(type: "bit", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LinkedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInPeople", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "TripImportLines",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SignInPersonId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PersonType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CostText = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    ManualMatchId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InvoiceOwnerId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InvoiceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InvoicedAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    LastReceivedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripImportLines", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "TripImports",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    WhenText = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastReceivedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSentBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    LastAppliedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAppliedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripImports", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateTable(
                name: "TripPayments",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    EntryId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    InvoiceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReceivedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPayments", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignInPeople_LinkedGuardianId",
                table: "SignInPeople",
                column: "LinkedGuardianId",
                unique: true,
                filter: "[LinkedGuardianId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SignInPeople_LinkedScoutId",
                table: "SignInPeople",
                column: "LinkedScoutId",
                unique: true,
                filter: "[LinkedScoutId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TripImportLines_InvoiceId",
                table: "TripImportLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TripImportLines_SignInPersonId",
                table: "TripImportLines",
                column: "SignInPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TripImports_StartDate",
                table: "TripImports",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_TripPayments_InvoiceId",
                table: "TripPayments",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignInPeople");

            migrationBuilder.DropTable(
                name: "TripImportLines");

            migrationBuilder.DropTable(
                name: "TripImports");

            migrationBuilder.DropTable(
                name: "TripPayments");
        }
    }
}
