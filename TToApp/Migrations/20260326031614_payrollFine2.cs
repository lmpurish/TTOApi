using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class payrollFine2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PayRunId",
                table: "PayrollFines",
                type: "int",
                nullable: true);

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
                name: "FK_PayrollFines_PayRun_PayRunId1",
                table: "PayrollFines",
                column: "PayRunId1",
                principalTable: "PayRun",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollFines_PayRun_PayRunId1",
                table: "PayrollFines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollFines_PayRunId1",
                table: "PayrollFines");

            migrationBuilder.DropColumn(
                name: "PayRunId",
                table: "PayrollFines");

            migrationBuilder.DropColumn(
                name: "PayRunId1",
                table: "PayrollFines");
        }
    }
}
