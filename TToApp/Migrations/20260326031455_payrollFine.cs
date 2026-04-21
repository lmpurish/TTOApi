using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class payrollFine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChargedAt",
                table: "PayrollFines",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargedAt",
                table: "PayrollFines");
        }
    }
}
