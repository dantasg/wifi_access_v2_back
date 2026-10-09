using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorreioEletronico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    AttachmentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IDCampaignRun = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentEmails_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SentEmails_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentEmails_IDCompany_SentAt",
                table: "SentEmails",
                columns: new[] { "IDCompany", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SentEmails_IDUnit",
                table: "SentEmails",
                column: "IDUnit");

            // Os PDFs de campanha que já saíram entram no Correio pelo registro da entrega. O assunto segue o
            // formato do serviço; o texto não foi guardado na época (Body nulo — a tela avisa).
            migrationBuilder.Sql("""
                INSERT INTO "SentEmails" ("Id", "IDCompany", "IDUnit", "UnitName", "Kind", "ToEmail", "Subject", "Body",
                    "AttachmentName", "SentAt", "IDCampaignRun", "PeriodStart", "PeriodEnd")
                SELECT gen_random_uuid(), r."IDCompany",
                    CASE WHEN EXISTS (SELECT 1 FROM "Units" u WHERE u."Id" = d."IDUnit") THEN d."IDUnit" END,
                    left(d."UnitName", 120), 'Campaign', left(d."Email", 200),
                    left('Campanha ' || c."Name" || ' — ' || d."UnitName" || ' — ' || to_char(r."LocalDate", 'DD/MM/YYYY')
                        || ' (' || d."RecipientCount" || CASE WHEN d."RecipientCount" = 1 THEN ' cliente)' ELSE ' clientes)' END, 400),
                    NULL, left(d."FileName", 200), d."SentAt", r."Id", NULL, NULL
                FROM "CampaignDeliveries" d
                JOIN "CampaignRuns" r ON r."Id" = d."IDRun"
                JOIN "Campaigns" c ON c."Id" = r."IDCampaign"
                WHERE d."Status" = 'Sent' AND d."SentAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentEmails");
        }
    }
}
