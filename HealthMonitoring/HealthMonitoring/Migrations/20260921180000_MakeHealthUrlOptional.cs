using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HealthMonitoring.Persistence;

#nullable disable

namespace HealthMonitoring.Migrations;

[DbContext(typeof(HealthMonitoringDbContext))]
[Migration("20260921180000_MakeHealthUrlOptional")]
public partial class MakeHealthUrlOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "HealthUrl",
            table: "MonitoredServices",
            type: "nvarchar(2048)",
            maxLength: 2048,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(2048)",
            oldMaxLength: 2048);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "HealthUrl",
            table: "MonitoredServices",
            type: "nvarchar(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "http://localhost/health",
            oldClrType: typeof(string),
            oldType: "nvarchar(2048)",
            oldMaxLength: 2048,
            oldNullable: true);
    }
}
