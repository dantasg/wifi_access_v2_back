using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppChangedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppChangedBy",
                table: "Companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppEnabled",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_AccessToken",
                table: "Companies",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_AppSecret",
                table: "Companies",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_BusinessAccountId",
                table: "Companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WhatsApp_CheckPassed",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsApp_CheckedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_DisplayName",
                table: "Companies",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_DisplayPhoneNumber",
                table: "Companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_LastError",
                table: "Companies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_MessagingLimit",
                table: "Companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_PhoneNumberId",
                table: "Companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_QualityRating",
                table: "Companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhatsApp_VerifyToken",
                table: "Companies",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WhatsAppAccountEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAccountEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppAccountEvents_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAccountEvents_IDCompany_CreatedAt",
                table: "WhatsAppAccountEvents",
                columns: new[] { "IDCompany", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppAccountEvents");

            migrationBuilder.DropColumn(
                name: "WhatsAppChangedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsAppChangedBy",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsAppEnabled",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_AccessToken",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_AppSecret",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_BusinessAccountId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_CheckPassed",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_CheckedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_DisplayName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_DisplayPhoneNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_LastError",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_MessagingLimit",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_PhoneNumberId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_QualityRating",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsApp_VerifyToken",
                table: "Companies");
        }
    }
}
