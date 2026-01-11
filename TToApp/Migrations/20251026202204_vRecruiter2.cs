using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class vRecruiter2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_RecruiterId",
                table: "Users",
                column: "RecruiterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_RecruiterId",
                table: "Users",
                column: "RecruiterId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_RecruiterId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_RecruiterId",
                table: "Users");
        }
    }
}
