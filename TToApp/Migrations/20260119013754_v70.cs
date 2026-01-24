using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v70 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RouteDate",
                table: "PayRunLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoneArea",
                table: "PayRunLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ZoneId",
                table: "PayRunLine",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RouteDate",
                table: "PayRunLine");

            migrationBuilder.DropColumn(
                name: "ZoneArea",
                table: "PayRunLine");

            migrationBuilder.DropColumn(
                name: "ZoneId",
                table: "PayRunLine");
        }
    }
}
