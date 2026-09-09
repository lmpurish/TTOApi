using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class FixPayrollFinePayRunIdType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_Packages_PackageId",
                table: "PayrollFines");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_PayRun_PayRunId1",
                table: "PayrollFines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollFines_PayRunId1",
                table: "PayrollFines");

            migrationBuilder.DropColumn(
                name: "PayRunId1",
                table: "PayrollFines");

            migrationBuilder.AlterColumn<long>(
                name: "PayRunId",
                table: "PayrollFines",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "PayrollFines",
                type: "decimal(18,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "UserId1",
                table: "PayrollFines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserWarehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWarehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFines_PayRunId",
                table: "PayrollFines",
                column: "PayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFines_UserId1",
                table: "PayrollFines",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_UserId_IsActive",
                table: "UserWarehouses",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_UserId_WarehouseId",
                table: "UserWarehouses",
                columns: new[] { "UserId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_WarehouseId",
                table: "UserWarehouses",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollFines_Packages_PackageId",
                table: "PayrollFines",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollFines_PayRun_PayRunId",
                table: "PayrollFines",
                column: "PayRunId",
                principalTable: "PayRun",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollFines_Users_UserId1",
                table: "PayrollFines",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_Packages_PackageId",
                table: "PayrollFines");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_PayRun_PayRunId",
                table: "PayrollFines");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_Users_UserId1",
                table: "PayrollFines");

            migrationBuilder.DropTable(
                name: "UserWarehouses");

            migrationBuilder.DropIndex(
                name: "IX_PayrollFines_PayRunId",
                table: "PayrollFines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollFines_UserId1",
                table: "PayrollFines");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "PayrollFines");

            migrationBuilder.AlterColumn<int>(
                name: "PayRunId",
                table: "PayrollFines",
                type: "int",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "PayrollFines",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddColumn<long>(
                name: "PayRunId1",
                table: "PayrollFines",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollFines_PayRunId1",
                table: "PayrollFines",
                column: "PayRunId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollFines_Packages_PackageId",
                table: "PayrollFines",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollFines_PayRun_PayRunId1",
                table: "PayrollFines",
                column: "PayRunId1",
                principalTable: "PayRun",
                principalColumn: "Id");
        }
    }
}
