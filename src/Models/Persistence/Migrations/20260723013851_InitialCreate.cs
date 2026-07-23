using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReportSendDay = table.Column<int>(type: "integer", nullable: false),
                    Unifi_Host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unifi_Site = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Unifi_Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unifi_Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unifi_UnifiOs = table.Column<bool>(type: "boolean", nullable: false),
                    Unifi_VerifySsl = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instagram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nascimento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    Ap = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    Ssid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Colors_Brand = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_BrandDark = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Surface = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Card = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Field = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Ink = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Muted = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Colors_Line = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Logo = table.Column<string>(type: "text", nullable: true),
                    Favicon = table.Column<string>(type: "text", nullable: true),
                    Banner = table.Column<string>(type: "text", nullable: true),
                    Ssid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccessMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalSettings_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_IDCompany_Timestamp",
                table: "Leads",
                columns: new[] { "IDCompany", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalSettings_IDCompany",
                table: "PortalSettings",
                column: "IDCompany",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IDCompany",
                table: "Users",
                column: "IDCompany");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "PortalSettings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
