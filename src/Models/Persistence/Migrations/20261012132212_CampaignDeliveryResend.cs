using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignDeliveryResend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignDeliveries_IDRun_IDUnit",
                table: "CampaignDeliveries");

            migrationBuilder.AddColumn<bool>(
                name: "IsResend",
                table: "CampaignDeliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                table: "CampaignDeliveries",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_IDRun_IDUnit",
                table: "CampaignDeliveries",
                columns: new[] { "IDRun", "IDUnit" },
                unique: true,
                filter: "\"IsResend\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignDeliveries_IDRun_IDUnit",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "IsResend",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "CampaignDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_IDRun_IDUnit",
                table: "CampaignDeliveries",
                columns: new[] { "IDRun", "IDUnit" },
                unique: true);
        }
    }
}
