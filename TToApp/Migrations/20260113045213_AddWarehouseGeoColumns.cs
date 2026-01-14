using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseGeoColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Warehouses', 'Latitude') IS NULL
    ALTER TABLE [Warehouses] ADD [Latitude] float NULL;

IF COL_LENGTH('Warehouses', 'Longitude') IS NULL
    ALTER TABLE [Warehouses] ADD [Longitude] float NULL;

IF COL_LENGTH('Warehouses', 'GeofenceRadiusMeters') IS NULL
    ALTER TABLE [Warehouses] ADD [GeofenceRadiusMeters] int NOT NULL CONSTRAINT DF_Warehouses_GeofenceRadiusMeters DEFAULT(200);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Warehouses', 'GeofenceRadiusMeters') IS NOT NULL
BEGIN
    DECLARE @df sysname;
    SELECT @df = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID('Warehouses')
      AND c.name = 'GeofenceRadiusMeters';

    IF @df IS NOT NULL EXEC('ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df + ']');
    ALTER TABLE [Warehouses] DROP COLUMN [GeofenceRadiusMeters];
END

IF COL_LENGTH('Warehouses', 'Longitude') IS NOT NULL
    ALTER TABLE [Warehouses] DROP COLUMN [Longitude];

IF COL_LENGTH('Warehouses', 'Latitude') IS NOT NULL
    ALTER TABLE [Warehouses] DROP COLUMN [Latitude];
");
        }
    }
}
