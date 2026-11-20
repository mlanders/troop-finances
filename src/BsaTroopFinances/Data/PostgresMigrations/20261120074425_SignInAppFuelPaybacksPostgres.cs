using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class SignInAppFuelPaybacksPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SourceAmount",
                table: "ReimbursementRequests",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "ReimbursementRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementRequests_SourceRef",
                table: "ReimbursementRequests",
                column: "SourceRef",
                unique: true);
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
