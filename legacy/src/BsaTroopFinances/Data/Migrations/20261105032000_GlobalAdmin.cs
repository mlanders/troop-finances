using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BsaTroopFinances.Data.Migrations
{
    /// <inheritdoc />
    public partial class GlobalAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Production already ran an earlier draft of this migration,
            // 20261105031138_GlobalAdminAndTemporaryEditAlert, which added IsGlobalAdmin plus an
            // EmailSettings.TemporaryEditEnabled switch that was then dropped from the code. So add
            // the column only where it's missing, remove the leftover switch and its default
            // constraint, and forget the draft's history row. Elsewhere this is a plain AddColumn.
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'AdminUsers', N'IsGlobalAdmin') IS NULL
                    ALTER TABLE [AdminUsers] ADD [IsGlobalAdmin] bit NOT NULL DEFAULT CAST(0 AS bit);
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'EmailSettings', N'TemporaryEditEnabled') IS NOT NULL
                BEGIN
                    DECLARE @constraint sysname = (
                        SELECT d.[name] FROM sys.default_constraints d
                        JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
                        WHERE d.[parent_object_id] = OBJECT_ID(N'EmailSettings') AND c.[name] = N'TemporaryEditEnabled');
                    IF @constraint IS NOT NULL
                        EXEC(N'ALTER TABLE [EmailSettings] DROP CONSTRAINT [' + @constraint + N']');
                    ALTER TABLE [EmailSettings] DROP COLUMN [TemporaryEditEnabled];
                END;
                """);

            migrationBuilder.Sql("""
                DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261105031138_GlobalAdminAndTemporaryEditAlert';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGlobalAdmin",
                table: "AdminUsers");
        }
    }
}
