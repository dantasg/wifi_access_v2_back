using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Company = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    City = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Stores = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UsesUnifi = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ConsentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UtmSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UtmMedium = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UtmCampaign = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UtmTerm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UtmContent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Gclid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Fbclid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LandingPage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Referrer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Page = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusChangedBy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifyAttempts = table.Column<int>(type: "integer", nullable: false),
                    NotifyError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteContacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteContacts_CreatedAt",
                table: "SiteContacts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SiteContacts_NotifiedAt",
                table: "SiteContacts",
                column: "NotifiedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteContacts");
        }
    }
}
