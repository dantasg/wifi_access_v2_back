using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispositivosDaUnidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DevicesSyncError",
                table: "Units",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DevicesSyncedAt",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnitDevices",
                columns: table => new
                {
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: false),
                    Mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitDevices", x => new { x.IDUnit, x.Mac });
                    table.ForeignKey(
                        name: "FK_UnitDevices_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitDevices_Mac",
                table: "UnitDevices",
                column: "Mac");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnitDevices");

            migrationBuilder.DropColumn(
                name: "DevicesSyncError",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "DevicesSyncedAt",
                table: "Units");
        }
    }
}
