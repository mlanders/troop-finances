using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopUpInvoiceIds",
                table: "TripImportLines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopUpInvoiceIds",
                table: "TripImportLines");
        }
    }
}
