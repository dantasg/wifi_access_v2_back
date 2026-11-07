using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppCampaignSend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppOptOutAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IDWhatsAppTemplate",
                table: "CampaignRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppEnabled",
                table: "CampaignRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppError",
                table: "CampaignRuns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppRetryAt",
                table: "CampaignRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppStartedAt",
                table: "CampaignRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppVersionNumber",
                table: "CampaignRuns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppWaitUntil",
                table: "CampaignRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppError",
                table: "CampaignRecipients",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppStatus",
                table: "CampaignRecipients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCustomer = table.Column<Guid>(type: "uuid", nullable: true),
                    IDCampaign = table.Column<Guid>(type: "uuid", nullable: true),
                    IDCampaignRun = table.Column<Guid>(type: "uuid", nullable: true),
                    IDCampaignRecipient = table.Column<long>(type: "bigint", nullable: true),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ToPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WaId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromPhoneNumberId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromDisplayPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IDWhatsAppTemplate = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TemplateLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    MetaMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetaMessageStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PricingCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Billable = table.Column<bool>(type: "boolean", nullable: true),
                    PricingType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_CampaignRecipients_IDCampaignRecipient",
                        column: x => x.IDCampaignRecipient,
                        principalTable: "CampaignRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_Customers_IDCustomer",
                        column: x => x.IDCustomer,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_IDCampaignRecipient",
                table: "WhatsAppMessages",
                column: "IDCampaignRecipient");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_IDCampaignRun",
                table: "WhatsAppMessages",
                column: "IDCampaignRun");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_IDCompany_CreatedAt",
                table: "WhatsAppMessages",
                columns: new[] { "IDCompany", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_IDCustomer",
                table: "WhatsAppMessages",
                column: "IDCustomer");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_MetaMessageId",
                table: "WhatsAppMessages",
                column: "MetaMessageId",
                unique: true,
                filter: "\"MetaMessageId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "WhatsAppOptOutAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IDWhatsAppTemplate",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppEnabled",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppError",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppRetryAt",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppStartedAt",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppVersionNumber",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppWaitUntil",
                table: "CampaignRuns");

            migrationBuilder.DropColumn(
                name: "WhatsAppError",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "WhatsAppStatus",
                table: "CampaignRecipients");
        }
    }
}
