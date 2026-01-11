using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v83 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dir",
                table: "UserUiSettings");

            migrationBuilder.DropColumn(
                name: "SidenavCollapsed",
                table: "UserUiSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dir",
                table: "UserUiSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SidenavCollapsed",
                table: "UserUiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
