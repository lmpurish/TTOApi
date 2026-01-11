using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v35 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MetroId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_MetroId",
                table: "Users",
                column: "MetroId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Metro_MetroId",
                table: "Users",
                column: "MetroId",
                principalTable: "Metro",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Metro_MetroId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_MetroId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MetroId",
                table: "Users");
        }
    }
}
