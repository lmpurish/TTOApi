using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class schedule2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_EventEvents",
                table: "EventEvents");

            migrationBuilder.RenameTable(
                name: "EventEvents",
                newName: "ScheduleEvents");

            migrationBuilder.AddColumn<DateTime>(
                name: "activityDate",
                table: "ApplicantActivity",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScheduleEvents",
                table: "ScheduleEvents",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ScheduleEvents",
                table: "ScheduleEvents");

            migrationBuilder.DropColumn(
                name: "activityDate",
                table: "ApplicantActivity");

            migrationBuilder.RenameTable(
                name: "ScheduleEvents",
                newName: "EventEvents");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EventEvents",
                table: "EventEvents",
                column: "Id");
        }
    }
}
