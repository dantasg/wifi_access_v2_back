using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRedirectUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedirectUrl",
                table: "PortalSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RedirectUrl",
                table: "PortalSettings");
        }
    }
}
