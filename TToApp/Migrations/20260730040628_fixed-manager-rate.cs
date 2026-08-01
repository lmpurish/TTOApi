using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class fixedmanagerrate : Migration
    {
        /// <inheritdoc />
         protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PaysManagerDailySalary",
                table: "UserWarehouses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ManagerDailyRate",
                table: "UserWarehouses",
                type: "decimal(18,2)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaysManagerDailySalary",
                table: "UserWarehouses");

            migrationBuilder.DropColumn(
                name: "ManagerDailyRate",
                table: "UserWarehouses");
        }
    }
}
