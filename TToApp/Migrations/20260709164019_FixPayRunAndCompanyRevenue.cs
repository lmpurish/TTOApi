using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    public partial class FixPayRunAndCompanyRevenue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyRevenues",
                columns: table => new
                {
                    Id = table.Column<long>(
                            type: "bigint",
                            nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),

                    CompanyId = table.Column<int>(
                        type: "int",
                        nullable: false),

                    PayPeriodId = table.Column<long>(
                        type: "bigint",
                        nullable: false),

                    WarehouseId = table.Column<int>(
                        type: "int",
                        nullable: true),

                    Revenue = table.Column<decimal>(
                        type: "decimal(18,2)",
                        nullable: false),

                    Expenses = table.Column<decimal>(
                        type: "decimal(18,2)",
                        nullable: false),

                    Adjustments = table.Column<decimal>(
                        type: "decimal(18,2)",
                        nullable: false),

                    RevenueType = table.Column<string>(
                        type: "nvarchar(50)",
                        maxLength: 50,
                        nullable: false),

                    Notes = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: true),

                    AttachmentUrl = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: true),

                    RevenueDate = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: false),

                    CreatedBy = table.Column<int>(
                        type: "int",
                        nullable: false),

                    CreatedAt = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: false),

                    UpdatedBy = table.Column<int>(
                        type: "int",
                        nullable: true),

                    UpdatedAt = table.Column<DateTime>(
                        type: "datetime2",
                        nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                         "PK_CompanyRevenues",
                         x => x.Id);

                    table.ForeignKey(
                        name: "FK_CompanyRevenues_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.ForeignKey(
                        name: "FK_CompanyRevenues_PayPeriod_PayPeriodId",
                        column: x => x.PayPeriodId,
                        principalTable: "PayPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_CompanyRevenues_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_CompanyId",
                table: "CompanyRevenues",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_PayPeriodId",
                table: "CompanyRevenues",
                column: "PayPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_WarehouseId",
                table: "CompanyRevenues",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_PayPeriodId_WarehouseId",
                table: "CompanyRevenues",
                columns: new[] { "PayPeriodId", "WarehouseId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyRevenues");
        }
    }
}