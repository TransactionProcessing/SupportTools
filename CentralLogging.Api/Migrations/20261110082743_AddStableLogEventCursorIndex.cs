using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralLogging.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStableLogEventCursorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LogEvents_TimestampUtc_EventId",
                schema: "dbo",
                table: "LogEvents",
                columns: new[] { "TimestampUtc", "EventId" },
                descending: new[] { true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEvents_TimestampUtc_EventId",
                schema: "dbo",
                table: "LogEvents");
        }
    }
}
