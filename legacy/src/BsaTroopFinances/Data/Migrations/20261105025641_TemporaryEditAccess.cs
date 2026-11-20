using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class TemporaryEditAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanTurnOnEdit",
                table: "AdminUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EditReason",
                table: "AdminUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditUntilUtc",
                table: "AdminUsers",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanTurnOnEdit",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "EditReason",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "EditUntilUtc",
                table: "AdminUsers");
        }
    }
}
