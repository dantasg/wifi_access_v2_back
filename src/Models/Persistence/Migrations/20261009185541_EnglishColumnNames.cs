using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnglishColumnNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Ddd",
                table: "Units",
                newName: "AreaCode");

            migrationBuilder.RenameColumn(
                name: "Ddd",
                table: "PortalSettings",
                newName: "AreaCode");

            migrationBuilder.RenameColumn(
                name: "Telefone",
                table: "Leads",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "Nome",
                table: "Leads",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "Nascimento",
                table: "Leads",
                newName: "BirthDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AreaCode",
                table: "Units",
                newName: "Ddd");

            migrationBuilder.RenameColumn(
                name: "AreaCode",
                table: "PortalSettings",
                newName: "Ddd");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "Leads",
                newName: "Telefone");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Leads",
                newName: "Nome");

            migrationBuilder.RenameColumn(
                name: "BirthDate",
                table: "Leads",
                newName: "Nascimento");
        }
    }
}
