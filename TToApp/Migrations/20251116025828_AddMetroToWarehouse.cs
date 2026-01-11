using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    public partial class AddMetroToWarehouse : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Agregar columna MetroId a Warehouses
            migrationBuilder.AddColumn<int>(
                name: "MetroId",
                table: "Warehouses",
                type: "int",
                nullable: true); // nullable porque en tu modelo es int?

            // 2) Crear índice para la FK
            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_MetroId",
                table: "Warehouses",
                column: "MetroId");

            // 3) Agregar la foreign key hacia Metro(Id)
            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_Metro_MetroId",
                table: "Warehouses",
                column: "MetroId",
                principalTable: "Metro",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            // o ReferentialAction.Restrict si prefieres
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_Metro_MetroId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_MetroId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "MetroId",
                table: "Warehouses");
        }
    }
}
