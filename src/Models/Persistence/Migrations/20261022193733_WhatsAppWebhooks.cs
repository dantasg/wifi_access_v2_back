using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IDWhatsAppOptOutMessage",
                table: "Customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppAlerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppAlerts_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppInboundMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCustomer = table.Column<Guid>(type: "uuid", nullable: false),
                    MetaMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    IsOptOut = table.Column<bool>(type: "boolean", nullable: false),
                    IDCampaign = table.Column<Guid>(type: "uuid", nullable: true),
                    IDCampaignRun = table.Column<Guid>(type: "uuid", nullable: true),
                    IDWhatsAppMessage = table.Column<long>(type: "bigint", nullable: true),
                    ContextMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppInboundMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppInboundMessages_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WhatsAppInboundMessages_Customers_IDCustomer",
                        column: x => x.IDCustomer,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WhatsAppInboundMessages_WhatsAppMessages_IDWhatsAppMessage",
                        column: x => x.IDWhatsAppMessage,
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDWhatsAppMessage = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MetaTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PricingCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PricingType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Billable = table.Column<bool>(type: "boolean", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessageEvents_WhatsAppMessages_IDWhatsAppMessage",
                        column: x => x.IDWhatsAppMessage,
                        principalTable: "WhatsAppMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppWebhookRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignatureValid = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Fields = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppWebhookRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppWebhookRequests_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAlerts_IDCompany_Key_CreatedAt",
                table: "WhatsAppAlerts",
                columns: new[] { "IDCompany", "Key", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAlerts_SentAt",
                table: "WhatsAppAlerts",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_IDCompany_SentAt",
                table: "WhatsAppInboundMessages",
                columns: new[] { "IDCompany", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_IDCustomer",
                table: "WhatsAppInboundMessages",
                column: "IDCustomer");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_IDWhatsAppMessage",
                table: "WhatsAppInboundMessages",
                column: "IDWhatsAppMessage");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_MetaMessageId",
                table: "WhatsAppInboundMessages",
                column: "MetaMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageEvents_IDWhatsAppMessage_Status",
                table: "WhatsAppMessageEvents",
                columns: new[] { "IDWhatsAppMessage", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookRequests_IDCompany_ReceivedAt",
                table: "WhatsAppWebhookRequests",
                columns: new[] { "IDCompany", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookRequests_ReceivedAt",
                table: "WhatsAppWebhookRequests",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppAlerts");

            migrationBuilder.DropTable(
                name: "WhatsAppInboundMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppMessageEvents");

            migrationBuilder.DropTable(
                name: "WhatsAppWebhookRequests");

            migrationBuilder.DropColumn(
                name: "IDWhatsAppOptOutMessage",
                table: "Customers");
        }
    }
}
