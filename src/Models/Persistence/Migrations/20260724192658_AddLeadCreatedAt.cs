using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Leads",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Leads já existentes: assume a data de cadastro igual ao último acesso conhecido.
            migrationBuilder.Sql(@"UPDATE ""Leads"" SET ""CreatedAt"" = ""Timestamp"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Leads");
        }
    }
}
