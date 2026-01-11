using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v61 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    EnableWeightExtra = table.Column<bool>(type: "bit", nullable: false),
                    EnablePenalties = table.Column<bool>(type: "bit", nullable: false),
                    EnableBonuses = table.Column<bool>(type: "bit", nullable: false),
                    DefaultPenaltyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PenaltyCapPerWeek = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollConfig", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollConfig_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollBonusRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollConfigId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBonusRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBonusRule_PayrollConfig_PayrollConfigId",
                        column: x => x.PayrollConfigId,
                        principalTable: "PayrollConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollPenaltyRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollConfigId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApplyPerOccurrence = table.Column<bool>(type: "bit", nullable: false),
                    MaxOccurrencesPerWeek = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPenaltyRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollPenaltyRule_PayrollConfig_PayrollConfigId",
                        column: x => x.PayrollConfigId,
                        principalTable: "PayrollConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollWeightRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollConfigId = table.Column<int>(type: "int", nullable: false),
                    MinWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ExtraAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollWeightRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollWeightRule_PayrollConfig_PayrollConfigId",
                        column: x => x.PayrollConfigId,
                        principalTable: "PayrollConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBonusRule_PayrollConfigId",
                table: "PayrollBonusRule",
                column: "PayrollConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfig_WarehouseId",
                table: "PayrollConfig",
                column: "WarehouseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPenaltyRule_PayrollConfigId_Type",
                table: "PayrollPenaltyRule",
                columns: new[] { "PayrollConfigId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollWeightRule_PayrollConfigId",
                table: "PayrollWeightRule",
                column: "PayrollConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBonusRule");

            migrationBuilder.DropTable(
                name: "PayrollPenaltyRule");

            migrationBuilder.DropTable(
                name: "PayrollWeightRule");

            migrationBuilder.DropTable(
                name: "PayrollConfig");
        }
    }
}
