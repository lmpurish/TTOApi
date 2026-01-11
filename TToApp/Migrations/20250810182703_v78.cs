using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v78 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverLicenceNumber",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SocialSecurityNumber",
                table: "UserProfiles");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DateOfBirth",
                table: "UserProfiles",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverLicenseNumber",
                table: "UserProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpDriverLicense",
                table: "UserProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnEncrypted",
                table: "UserProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnLast4",
                table: "UserProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SsnUpdatedAt",
                table: "UserProfiles",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverLicenseNumber",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ExpDriverLicense",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SsnEncrypted",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SsnLast4",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SsnUpdatedAt",
                table: "UserProfiles");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "UserProfiles",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverLicenceNumber",
                table: "UserProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocialSecurityNumber",
                table: "UserProfiles",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);
        }
    }
}
