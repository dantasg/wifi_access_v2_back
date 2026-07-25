using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueLeadPerDeviceUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Leads_IDUnit_Mac",
                table: "Leads",
                columns: new[] { "IDUnit", "Mac" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_IDUnit_Mac",
                table: "Leads");
        }
    }
}
