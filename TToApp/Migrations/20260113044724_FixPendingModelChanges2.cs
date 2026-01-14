using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingModelChanges2 : Migration
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
         
        }
    }
}
