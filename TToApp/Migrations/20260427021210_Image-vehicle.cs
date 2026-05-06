using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    public partial class Imagevehicle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImage_Companies_CompanyId",
                table: "VehicleImage");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImage_RentalVehicles_RentalVehicleId",
                table: "VehicleImage");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImage_Vehicles_VehicleId",
                table: "VehicleImage");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VehicleImage",
                table: "VehicleImage");

            migrationBuilder.RenameTable(
                name: "VehicleImage",
                newName: "VehicleImages");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImage_VehicleId",
                table: "VehicleImages",
                newName: "IX_VehicleImages_VehicleId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImage_RentalVehicleId",
                table: "VehicleImages",
                newName: "IX_VehicleImages_RentalVehicleId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImage_CompanyId",
                table: "VehicleImages",
                newName: "IX_VehicleImages_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_VehicleImages",
                table: "VehicleImages",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImages_Companies_CompanyId",
                table: "VehicleImages",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImages_RentalVehicles_RentalVehicleId",
                table: "VehicleImages",
                column: "RentalVehicleId",
                principalTable: "RentalVehicles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImages_Vehicles_VehicleId",
                table: "VehicleImages",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_Companies_CompanyId",
                table: "VehicleImages");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_RentalVehicles_RentalVehicleId",
                table: "VehicleImages");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_Vehicles_VehicleId",
                table: "VehicleImages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VehicleImages",
                table: "VehicleImages");

            migrationBuilder.RenameTable(
                name: "VehicleImages",
                newName: "VehicleImage");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImages_VehicleId",
                table: "VehicleImage",
                newName: "IX_VehicleImage_VehicleId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImages_RentalVehicleId",
                table: "VehicleImage",
                newName: "IX_VehicleImage_RentalVehicleId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleImages_CompanyId",
                table: "VehicleImage",
                newName: "IX_VehicleImage_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_VehicleImage",
                table: "VehicleImage",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImage_Companies_CompanyId",
                table: "VehicleImage",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImage_RentalVehicles_RentalVehicleId",
                table: "VehicleImage",
                column: "RentalVehicleId",
                principalTable: "RentalVehicles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImage_Vehicles_VehicleId",
                table: "VehicleImage",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}