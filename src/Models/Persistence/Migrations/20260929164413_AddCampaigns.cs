using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Models.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "America/Belem");

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyCampaignKinds",
                columns: table => new
                {
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnabledBy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCampaignKinds", x => new { x.IDCompany, x.Kind });
                    table.ForeignKey(
                        name: "FK_CompanyCampaignKinds_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instagram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FirstVisitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstVisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastVisitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    VisitCount = table.Column<int>(type: "integer", nullable: false),
                    IDLastUnit = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Companies_IDCompany",
                        column: x => x.IDCompany,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDCampaign = table.Column<Guid>(type: "uuid", nullable: false),
                    IDRun = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IDUser = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignEvents_Campaigns_IDCampaign",
                        column: x => x.IDCampaign,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCampaign = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    Changes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IDUser = table.Column<Guid>(type: "uuid", nullable: true),
                    Username = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignVersions_Campaigns_IDCampaign",
                        column: x => x.IDCampaign,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerUnits",
                columns: table => new
                {
                    IDCustomer = table.Column<Guid>(type: "uuid", nullable: false),
                    IDUnit = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstVisitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVisitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerUnits", x => new { x.IDCustomer, x.IDUnit });
                    table.ForeignKey(
                        name: "FK_CustomerUnits_Customers_IDCustomer",
                        column: x => x.IDCustomer,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerUnits_Units_IDUnit",
                        column: x => x.IDUnit,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCampaign = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCompany = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCampaignVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Simulation = table.Column<bool>(type: "boolean", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    SimulatedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    IgnoredCount = table.Column<int>(type: "integer", nullable: false),
                    CancelledCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRuns_CampaignVersions_IDCampaignVersion",
                        column: x => x.IDCampaignVersion,
                        principalTable: "CampaignVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CampaignRuns_Campaigns_IDCampaign",
                        column: x => x.IDCampaign,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IDRun = table.Column<Guid>(type: "uuid", nullable: false),
                    IDCustomer = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Milestone = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_CampaignRuns_IDRun",
                        column: x => x.IDRun,
                        principalTable: "CampaignRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_Customers_IDCustomer",
                        column: x => x.IDCustomer,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignEvents_IDCampaign_CreatedAt",
                table: "CampaignEvents",
                columns: new[] { "IDCampaign", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_IDCustomer",
                table: "CampaignRecipients",
                column: "IDCustomer");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_IDRun_IDCustomer",
                table: "CampaignRecipients",
                columns: new[] { "IDRun", "IDCustomer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_IDRun_Status_Id",
                table: "CampaignRecipients",
                columns: new[] { "IDRun", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_IDCampaign_LocalDate",
                table: "CampaignRuns",
                columns: new[] { "IDCampaign", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_IDCampaignVersion",
                table: "CampaignRuns",
                column: "IDCampaignVersion");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_IDCompany_LocalDate",
                table: "CampaignRuns",
                columns: new[] { "IDCompany", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRuns_Status",
                table: "CampaignRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_IDCompany",
                table: "Campaigns",
                column: "IDCompany");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status_NextRunAt",
                table: "Campaigns",
                columns: new[] { "Status", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignVersions_IDCampaign_Number",
                table: "CampaignVersions",
                columns: new[] { "IDCampaign", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IDCompany_FirstVisitDate",
                table: "Customers",
                columns: new[] { "IDCompany", "FirstVisitDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IDCompany_LastVisitAt",
                table: "Customers",
                columns: new[] { "IDCompany", "LastVisitAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IDCompany_Phone",
                table: "Customers",
                columns: new[] { "IDCompany", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerUnits_IDUnit",
                table: "CustomerUnits",
                column: "IDUnit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignEvents");

            migrationBuilder.DropTable(
                name: "CampaignRecipients");

            migrationBuilder.DropTable(
                name: "CompanyCampaignKinds");

            migrationBuilder.DropTable(
                name: "CustomerUnits");

            migrationBuilder.DropTable(
                name: "CampaignRuns");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "CampaignVersions");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Companies");
        }
    }
}
