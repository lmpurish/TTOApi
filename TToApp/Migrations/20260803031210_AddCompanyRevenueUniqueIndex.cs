using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyRevenueUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyRevenues_CompanyId",
                table: "CompanyRevenues");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_CompanyId_PayPeriodId_WarehouseId_RevenueType",
                table: "CompanyRevenues",
                columns: new[] { "CompanyId", "PayPeriodId", "WarehouseId", "RevenueType" },
                unique: true,
                filter: "[WarehouseId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyRevenues_CompanyId_PayPeriodId_WarehouseId_RevenueType",
                table: "CompanyRevenues");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRevenues_CompanyId",
                table: "CompanyRevenues",
                column: "CompanyId");
        }
    }
}
