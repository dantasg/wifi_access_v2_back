using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifiVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Visits",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // As visitas que já existem vieram todas do portal.
                defaultValue: "Portal");

            migrationBuilder.AddColumn<int>(
                name: "StaySeconds",
                table: "Visits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnifiWebhookCreatedAt",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnifiWebhookHash",
                table: "Units",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UnifiWebhookLastAt",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnitIgnoredDevices",
                columns: table => new
                {
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: false),
                    Mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitIgnoredDevices", x => new { x.IDUnit, x.Mac });
                    table.ForeignKey(
                        name: "FK_UnitIgnoredDevices_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WifiPresences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: false),
                    Mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StayStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Connects = table.Column<int>(type: "integer", nullable: false),
                    Disconnects = table.Column<int>(type: "integer", nullable: false),
                    Ap = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IDCustomer = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WifiPresences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WifiPresences_Customers_IDCustomer",
                        column: x => x.IDCustomer,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WifiPresences_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Units_UnifiWebhookHash",
                table: "Units",
                column: "UnifiWebhookHash",
                unique: true,
                filter: "\"UnifiWebhookHash\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_WifiPresences_IDCustomer",
                table: "WifiPresences",
                column: "IDCustomer");

            migrationBuilder.CreateIndex(
                name: "IX_WifiPresences_IDUnit_LocalDate",
                table: "WifiPresences",
                columns: new[] { "IDUnit", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WifiPresences_IDUnit_Mac_LocalDate",
                table: "WifiPresences",
                columns: new[] { "IDUnit", "Mac", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WifiPresences_LastSeenAt",
                table: "WifiPresences",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnitIgnoredDevices");

            migrationBuilder.DropTable(
                name: "WifiPresences");

            migrationBuilder.DropIndex(
                name: "IX_Units_UnifiWebhookHash",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "StaySeconds",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "UnifiWebhookCreatedAt",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "UnifiWebhookHash",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "UnifiWebhookLastAt",
                table: "Units");
        }
    }
}
