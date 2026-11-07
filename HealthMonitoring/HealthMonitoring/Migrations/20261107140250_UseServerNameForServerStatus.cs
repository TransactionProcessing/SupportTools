using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class UseServerNameForServerStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [ServerStatus]
                    WHERE NULLIF(LTRIM(RTRIM([ServerName])), N'') IS NULL)
                    THROW 51000, 'ServerStatus migration cannot continue: a report has an empty server name. Set a unique server name for each report and retry.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [ServerStatus]
                    GROUP BY LTRIM(RTRIM([ServerName]))
                    HAVING COUNT_BIG(*) > 1)
                    THROW 51000, 'ServerStatus migration cannot continue: server names are duplicated after trimming. Reconcile the rows to one unique server name per server and retry.', 1;

                UPDATE [ServerStatus]
                SET [ServerName] = LTRIM(RTRIM([ServerName]));
                """);

            migrationBuilder.DropIndex(
                name: "IX_ServerStatus_ServerId",
                table: "ServerStatus");

            migrationBuilder.DropColumn(
                name: "ServerId",
                table: "ServerStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatus_ServerName",
                table: "ServerStatus",
                column: "ServerName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ServerStatus])
                    THROW 51000, 'ServerStatus migration cannot be rolled back automatically because the original ServerId values were removed. Restore a database backup from before the migration or manually map each server name to its original ServerId before retrying.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_ServerStatus_ServerName",
                table: "ServerStatus");

            migrationBuilder.AddColumn<string>(
                name: "ServerId",
                table: "ServerStatus",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatus_ServerId",
                table: "ServerStatus",
                column: "ServerId",
                unique: true);
        }
    }
}
