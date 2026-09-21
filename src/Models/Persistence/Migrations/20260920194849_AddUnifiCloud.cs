using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unifi_ApiKey",
                table: "Units",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_ConsoleId",
                table: "Units",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_Mode",
                table: "Units",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // "Local": as unidades que já existem continuam falando direto com a controladora.
                // O EF geraria "" aqui, que funcionaria por acidente (só "Cloud" muda o caminho),
                // mas deixaria o banco com um modo em branco, impossível de interpretar depois.
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "Unifi_SiteId",
                table: "Units",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unifi_ApiKey",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Unifi_ConsoleId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Unifi_Mode",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Unifi_SiteId",
                table: "Units");
        }
    }
}
