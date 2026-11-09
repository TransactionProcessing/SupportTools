using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralLogging.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlignStructuredFieldServiceNameCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE dbo.ServiceStructuredFields DROP CONSTRAINT PK_ServiceStructuredFields;
                ALTER TABLE dbo.ServiceStructuredFields
                    ALTER COLUMN ServiceName nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL;
                ALTER TABLE dbo.ServiceStructuredFields
                    ADD CONSTRAINT PK_ServiceStructuredFields PRIMARY KEY CLUSTERED (ServiceName, FieldName, JsonType);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE dbo.ServiceStructuredFields DROP CONSTRAINT PK_ServiceStructuredFields;
                ALTER TABLE dbo.ServiceStructuredFields
                    ALTER COLUMN ServiceName nvarchar(200) COLLATE DATABASE_DEFAULT NOT NULL;
                ALTER TABLE dbo.ServiceStructuredFields
                    ADD CONSTRAINT PK_ServiceStructuredFields PRIMARY KEY CLUSTERED (ServiceName, FieldName, JsonType);
                """);
        }
    }
}
