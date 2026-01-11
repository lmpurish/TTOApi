using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v55 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RouteCode",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Routes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_WarehouseId",
                table: "Routes",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_Warehouses_WarehouseId",
                table: "Routes",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_Warehouses_WarehouseId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_WarehouseId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "RouteCode",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Routes");
        }
    }
}
