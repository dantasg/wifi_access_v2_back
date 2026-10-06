using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampanhasPorEmailDaUnidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Units",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportSentAt",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            // D24: o e-mail do relatório sai da empresa e vai para cada unidade dela (com o carimbo do
            // último envio, para não reenviar no mesmo mês). Copia antes de apagar as colunas.
            migrationBuilder.Sql(@"
                UPDATE ""Units"" AS u
                SET ""Email"" = COALESCE(c.""ReportEmail"", ''),
                    ""LastReportSentAt"" = c.""LastReportSentAt""
                FROM ""Companies"" AS c
                WHERE c.""Id"" = u.""IDCompany"";");

            migrationBuilder.DropColumn(
                name: "LastReportSentAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ReportEmail",
                table: "Companies");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "CampaignRecipients",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IDUnit",
                table: "CampaignRecipients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Info",
                table: "CampaignRecipients",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Instagram",
                table: "CampaignRecipients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CampaignDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDRun = table.Column<Guid>(type: "uuid", nullable: false),
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: true),
                    UnitName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignDeliveries_CampaignRuns_IDRun",
                        column: x => x.IDRun,
                        principalTable: "CampaignRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignDeliveries_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_IDRun_IDUnit",
                table: "CampaignDeliveries",
                columns: new[] { "IDRun", "IDUnit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_IDUnit",
                table: "CampaignDeliveries",
                column: "IDUnit");

            // Destinatários antigos: a unidade da última visita do cliente (para o histórico mostrar).
            migrationBuilder.Sql(@"
                UPDATE ""CampaignRecipients"" AS r
                SET ""IDUnit"" = c.""IDLastUnit""
                FROM ""Customers"" AS c
                WHERE c.""Id"" = r.""IDCustomer"" AND r.""IDUnit"" IS NULL;");

            // D22: a campanha de boas-vindas sai do sistema, com o histórico (versões, execuções,
            // destinatários e ações vão juntos pelas chaves em cascata) e a liberação nas empresas.
            migrationBuilder.Sql(@"DELETE FROM ""Campaigns"" WHERE ""Kind"" = 'Welcome';");
            migrationBuilder.Sql(@"DELETE FROM ""CompanyCampaignKinds"" WHERE ""Kind"" = 'Welcome';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "IDUnit",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "Info",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "Instagram",
                table: "CampaignRecipients");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportSentAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportEmail",
                table: "Companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Volta o e-mail para a empresa (o da primeira unidade que tiver). As boas-vindas não voltam.
            migrationBuilder.Sql(@"
                UPDATE ""Companies"" AS c
                SET ""ReportEmail"" = (
                    SELECT u.""Email"" FROM ""Units"" AS u
                    WHERE u.""IDCompany"" = c.""Id"" AND u.""Email"" <> ''
                    ORDER BY u.""Name"" LIMIT 1);");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "LastReportSentAt",
                table: "Units");
        }
    }
}
