using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Companies_IDCompany",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Unifi_Host",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Unifi_Password",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Unifi_Site",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Unifi_UnifiOs",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Unifi_Username",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Unifi_VerifySsl",
                table: "Companies");

            migrationBuilder.RenameColumn(
                name: "IDCompany",
                table: "Leads",
                newName: "IDUnit");

            migrationBuilder.RenameIndex(
                name: "IX_Leads_IDCompany_Timestamp",
                table: "Leads",
                newName: "IX_Leads_IDUnit_Timestamp");

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Unifi_Host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unifi_Site = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Unifi_Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unifi_Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unifi_UnifiOs = table.Column<bool>(type: "boolean", nullable: false),
                    Unifi_VerifySsl = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Units_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Units_IDCompany",
                table: "Units",
                column: "IDCompany");

            migrationBuilder.CreateIndex(
                name: "IX_Units_Slug",
                table: "Units",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Units_IDUnit",
                table: "Leads",
                column: "IDUnit",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Units_IDUnit",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.RenameColumn(
                name: "IDUnit",
                table: "Leads",
                newName: "IDCompany");

            migrationBuilder.RenameIndex(
                name: "IX_Leads_IDUnit_Timestamp",
                table: "Leads",
                newName: "IX_Leads_IDCompany_Timestamp");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_Host",
                table: "Companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_Password",
                table: "Companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_Site",
                table: "Companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Unifi_UnifiOs",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Unifi_Username",
                table: "Companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Unifi_VerifySsl",
                table: "Companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Companies_IDCompany",
                table: "Leads",
                column: "IDCompany",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
