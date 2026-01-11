using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverRate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DriverId = table.Column<long>(type: "bigint", nullable: false),
                    RateType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MinPayPerRoute = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    OverStopBonusThreshold = table.Column<int>(type: "int", nullable: true),
                    OverStopBonusPerStop = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    FailedStopPenalty = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    RescueStopRate = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    NightDeliveryBonus = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverRate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayPeriod",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Open"),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayPeriod", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayRun",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    DriverId = table.Column<long>(type: "bigint", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    Adjustments = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    NetAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false, computedColumnSql: "[GrossAmount] + [Adjustments]", stored: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CalculatedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRun", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayRun_PayPeriod_PayPeriodId",
                        column: x => x.PayPeriodId,
                        principalTable: "PayPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollAdjustment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayRunId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustment_PayRun_PayRunId",
                        column: x => x.PayRunId,
                        principalTable: "PayRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayRunLine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayRunId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 1m),
                    Rate = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false, computedColumnSql: "[Qty] * [Rate]", stored: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRunLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayRunLine_PayRun_PayRunId",
                        column: x => x.PayRunId,
                        principalTable: "PayRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustment_PayRunId",
                table: "PayrollAdjustment",
                column: "PayRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRun_PayPeriodId",
                table: "PayRun",
                column: "PayPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_PayRunLine_PayRunId",
                table: "PayRunLine",
                column: "PayRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverRate");

            migrationBuilder.DropTable(
                name: "PayrollAdjustment");

            migrationBuilder.DropTable(
                name: "PayRunLine");

            migrationBuilder.DropTable(
                name: "PayRun");

            migrationBuilder.DropTable(
                name: "PayPeriod");
        }
    }
}
