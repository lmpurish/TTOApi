using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetTokenToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DriverRate_Warehouses_WarehouseId",
                table: "DriverRate");

            migrationBuilder.DropTable(
                name: "Incidences");

            migrationBuilder.DropIndex(
                name: "IX_DriverRate_DriverId_WarehouseId_EffectiveFrom",
                table: "DriverRate");

            migrationBuilder.DropIndex(
                name: "IX_DriverRate_WarehouseId",
                table: "DriverRate");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "DriverRate");

            migrationBuilder.RenameColumn(
                name: "isActive",
                table: "Warehouses",
                newName: "IsActive");

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetToken",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiresAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "InstallmentAmount",
                table: "EmployeeLoans",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordResetToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAt",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "Warehouses",
                newName: "isActive");

            migrationBuilder.AlterColumn<decimal>(
                name: "InstallmentAmount",
                table: "EmployeeLoans",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "DriverRate",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Incidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidences_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Incidences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverRate_DriverId_WarehouseId_EffectiveFrom",
                table: "DriverRate",
                columns: new[] { "DriverId", "WarehouseId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverRate_WarehouseId",
                table: "DriverRate",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidences_RouteId",
                table: "Incidences",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidences_UserId",
                table: "Incidences",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DriverRate_Warehouses_WarehouseId",
                table: "DriverRate",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }
    }
}
