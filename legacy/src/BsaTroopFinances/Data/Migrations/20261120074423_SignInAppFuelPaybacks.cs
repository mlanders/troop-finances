using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class SignInAppFuelPaybacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SourceAmount",
                table: "ReimbursementRequests",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "ReimbursementRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_SourceRef",
                table: "ReimbursementRequests",
                column: "SourceRef",
                unique: true,
                filter: "[SourceRef] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReimbursementRequests_SourceRef",
                table: "ReimbursementRequests");

            migrationBuilder.DropColumn(
                name: "SourceAmount",
                table: "ReimbursementRequests");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "ReimbursementRequests");
        }
    }
}
