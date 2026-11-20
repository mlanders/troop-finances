using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminSignIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminSignIns",
                columns: table => new
                {
                    PartitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    UserKey = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    VerifiedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSignIns", x => new { x.PartitionKey, x.RowKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminSignIns_UserKey",
                table: "AdminSignIns",
                column: "UserKey");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSignIns_VerifiedEmail",
                table: "AdminSignIns",
                column: "VerifiedEmail");

            // Each existing person signed in with exactly one account: the one they're keyed by.
            // The verified address isn't known yet; it's filled in on that account's next sign-in.
            migrationBuilder.Sql("""
                INSERT INTO [AdminSignIns] ([PartitionKey], [RowKey], [UserKey], [Provider], [Name], [Email], [VerifiedEmail], [FirstSeenUtc], [LastSeenUtc])
                SELECT 'AdminSignIn', [RowKey], [RowKey], [Provider], [Name], [Email], '', [FirstSeenUtc], [LastSeenUtc]
                FROM [AdminUsers];
                """);

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AdminUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AdminUsers",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE u SET [Provider] = s.[Provider]
                FROM [AdminUsers] u JOIN [AdminSignIns] s ON s.[RowKey] = u.[RowKey];
                """);

            migrationBuilder.DropTable(
                name: "AdminSignIns");
        }
    }
}
