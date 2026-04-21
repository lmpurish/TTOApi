using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class RentalV1_Fixed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalVehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    MetroId = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StockNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Make = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Trim = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Transmission = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FuelType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SeatingCapacity = table.Column<int>(type: "int", nullable: true),
                    TrunkNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DailyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WeeklyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Plate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FacilityLocation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    MainImageUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GpsInstalled = table.Column<bool>(type: "bit", nullable: false),
                    DashCamInstalled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalVehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RentalVehicles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RentalVehicles_Metro_MetroId",
                        column: x => x.MetroId,
                        principalTable: "Metro",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VehicleImage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsCover = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ImageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RentalVehicleId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleImage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleImage_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleImage_RentalVehicles_RentalVehicleId",
                        column: x => x.RentalVehicleId,
                        principalTable: "RentalVehicles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VehicleImage_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RentalVehicles_CompanyId",
                table: "RentalVehicles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RentalVehicles_MetroId",
                table: "RentalVehicles",
                column: "MetroId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleImage_CompanyId",
                table: "VehicleImage",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleImage_RentalVehicleId",
                table: "VehicleImage",
                column: "RentalVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleImage_VehicleId",
                table: "VehicleImage",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleImage");

            migrationBuilder.DropTable(
                name: "RentalVehicles");
        }
    }
}
