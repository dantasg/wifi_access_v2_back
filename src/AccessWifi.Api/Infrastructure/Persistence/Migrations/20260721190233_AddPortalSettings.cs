using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessWifi.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortalSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
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
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortalSettings");
        }
    }
}
