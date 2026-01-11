using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TToApp.Migrations
{
    /// <inheritdoc />
    public partial class v93 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyDocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RequireSignature = table.Column<bool>(type: "bit", nullable: false),
                    IsMandatoryForAllUsers = table.Column<bool>(type: "bit", nullable: false),
                    RequiredRolesCsv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignaturePage = table.Column<int>(type: "int", nullable: false),
                    SignatureX = table.Column<float>(type: "real", nullable: false),
                    SignatureY = table.Column<float>(type: "real", nullable: false),
                    SignatureWidth = table.Column<float>(type: "real", nullable: false),
                    SignatureHeight = table.Column<float>(type: "real", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyDocumentTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyDocumentTemplates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyDocumentAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyDocumentTemplateId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revoked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyDocumentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyDocumentAssignments_CompanyDocumentTemplates_CompanyDocumentTemplateId",
                        column: x => x.CompanyDocumentTemplateId,
                        principalTable: "CompanyDocumentTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyDocumentAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserDocumentSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyDocumentTemplateId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    DrawnSignatureImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignedPdfUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentHashSha256 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SignerFullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignerEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignerIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignerUserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeoInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDocumentSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDocumentSignatures_CompanyDocumentTemplates_CompanyDocumentTemplateId",
                        column: x => x.CompanyDocumentTemplateId,
                        principalTable: "CompanyDocumentTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserDocumentSignatures_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocumentAssignments_CompanyDocumentTemplateId",
                table: "CompanyDocumentAssignments",
                column: "CompanyDocumentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocumentAssignments_UserId",
                table: "CompanyDocumentAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocumentTemplates_CompanyId_IsActive_Version",
                table: "CompanyDocumentTemplates",
                columns: new[] { "CompanyId", "IsActive", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDocumentSignatures_CompanyDocumentTemplateId",
                table: "UserDocumentSignatures",
                column: "CompanyDocumentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDocumentSignatures_CompanyId_UserId_CompanyDocumentTemplateId",
                table: "UserDocumentSignatures",
                columns: new[] { "CompanyId", "UserId", "CompanyDocumentTemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDocumentSignatures_UserId",
                table: "UserDocumentSignatures",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyDocumentAssignments");

            migrationBuilder.DropTable(
                name: "UserDocumentSignatures");

            migrationBuilder.DropTable(
                name: "CompanyDocumentTemplates");
        }
    }
}
