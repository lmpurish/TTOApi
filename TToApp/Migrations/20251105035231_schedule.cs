using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class schedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApplicantActivity_RecruiterId",
                table: "ApplicantActivity",
                column: "RecruiterId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicantActivity_Users_RecruiterId",
                table: "ApplicantActivity",
                column: "RecruiterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicantActivity_Users_RecruiterId",
                table: "ApplicantActivity");

            migrationBuilder.DropIndex(
                name: "IX_ApplicantActivity_RecruiterId",
                table: "ApplicantActivity");
        }
    }
}
