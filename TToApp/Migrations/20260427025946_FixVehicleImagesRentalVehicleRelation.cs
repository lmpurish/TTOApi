using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class FixVehicleImagesRentalVehicleRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_RentalVehicles_RentalVehicleId",
                table: "VehicleImages");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_Vehicles_VehicleId",
                table: "VehicleImages");

            migrationBuilder.DropIndex(
                name: "IX_VehicleImages_RentalVehicleId",
                table: "VehicleImages");

            migrationBuilder.DropColumn(
                name: "RentalVehicleId",
                table: "VehicleImages");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleImages_RentalVehicles_VehicleId",
                table: "VehicleImages",
                column: "VehicleId",
                principalTable: "RentalVehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleImages_RentalVehicles_VehicleId",
                table: "VehicleImages");

            migrationBuilder.AddColumn<int>(
                name: "RentalVehicleId",
                table: "VehicleImages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleImages_RentalVehicleId",
                table: "VehicleImages",
                column: "RentalVehicleId");

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
    }
}
