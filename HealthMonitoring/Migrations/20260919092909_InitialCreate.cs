using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitoredServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AggregateStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseDuration = table.Column<long>(type: "bigint", nullable: false),
                    RawResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoredServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonitorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HealthUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ConnectionString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IgnoreCertificateErrors = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PollingInterval = table.Column<long>(type: "bigint", nullable: false),
                    RequestTimeout = table.Column<long>(type: "bigint", nullable: false),
                    RetentionPeriod = table.Column<long>(type: "bigint", nullable: false),
                    StatusPolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Host = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitoredServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceStatusSnapshots",
                columns: table => new
                {
                    MonitoredServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastResponseDuration = table.Column<long>(type: "bigint", nullable: true),
                    CurrentIncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceStatusSnapshots", x => x.MonitoredServiceId);
                });

            migrationBuilder.CreateTable(
                name: "HealthCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HealthObservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Duration = table.Column<long>(type: "bigint", nullable: false),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthCheckResults_HealthObservations_HealthObservationId",
                        column: x => x.HealthObservationId,
                        principalTable: "HealthObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceDependencyLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonitoredServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DependencyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TargetMonitoredServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDependencyLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceDependencyLinks_MonitoredServices_MonitoredServiceId",
                        column: x => x.MonitoredServiceId,
                        principalTable: "MonitoredServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceDependencyLinks_MonitoredServices_TargetMonitoredServiceId",
                        column: x => x.TargetMonitoredServiceId,
                        principalTable: "MonitoredServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_HealthObservationId",
                table: "HealthCheckResults",
                column: "HealthObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthObservations_MonitoredServiceId_ObservedAtUtc",
                table: "HealthObservations",
                columns: new[] { "MonitoredServiceId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredServices_ServiceId",
                table: "MonitoredServices",
                column: "ServiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDependencyLinks_MonitoredServiceId_DependencyName",
                table: "ServiceDependencyLinks",
                columns: new[] { "MonitoredServiceId", "DependencyName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDependencyLinks_TargetMonitoredServiceId",
                table: "ServiceDependencyLinks",
                column: "TargetMonitoredServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceIncidents_MonitoredServiceId_StartedAtUtc",
                table: "ServiceIncidents",
                columns: new[] { "MonitoredServiceId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthCheckResults");

            migrationBuilder.DropTable(
                name: "ServiceDependencyLinks");

            migrationBuilder.DropTable(
                name: "ServiceIncidents");

            migrationBuilder.DropTable(
                name: "ServiceStatusSnapshots");

            migrationBuilder.DropTable(
                name: "HealthObservations");

            migrationBuilder.DropTable(
                name: "MonitoredServices");
        }
    }
}
