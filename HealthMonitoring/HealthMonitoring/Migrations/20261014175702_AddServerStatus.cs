using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddServerStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerStatus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OverallStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChecksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerStatus", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatus_ServerId",
                table: "ServerStatus",
                column: "ServerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerStatus");
        }
    }
}
