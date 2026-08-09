using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Campaign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyLimitPerAgent = table.Column<int>(type: "int", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowsTotal = table.Column<int>(type: "int", nullable: false),
                    RowsMatched = table.Column<int>(type: "int", nullable: false),
                    RowsUnmatched = table.Column<int>(type: "int", nullable: false),
                    RowsInvalid = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RewardGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerNameAtGrant = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByExternalUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardGrants_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RewardGrants_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    RawLine = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", nullable: true),
                    MatchedGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseResults", x => x.Id);
                    table.CheckConstraint("CK_PurchaseResults_AmountAndCurrencyTogether", "([Amount] IS NULL AND [Currency] IS NULL) OR ([Amount] IS NOT NULL AND [Currency] IS NOT NULL)");
                    table.CheckConstraint("CK_PurchaseResults_RequiredFieldsUnlessInvalid", "[MatchStatus] = 'Invalid' OR ([CustomerExternalId] IS NOT NULL AND [PurchaseDate] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PurchaseResults_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseResults_RewardGrants_MatchedGrantId",
                        column: x => x.MatchedGrantId,
                        principalTable: "RewardGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Agents_ExternalUserId",
                table: "Agents",
                column: "ExternalUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_Campaign_FileSha256",
                table: "ImportBatches",
                columns: new[] { "CampaignId", "FileSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseResults_BatchId",
                table: "PurchaseResults",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseResults_MatchedGrantId",
                table: "PurchaseResults",
                column: "MatchedGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardGrants_Agent_Campaign_BusinessDate_Status",
                table: "RewardGrants",
                columns: new[] { "AgentId", "CampaignId", "BusinessDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_RewardGrants_Agent_IdempotencyKey",
                table: "RewardGrants",
                columns: new[] { "AgentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RewardGrants_Campaign_Customer_Active",
                table: "RewardGrants",
                columns: new[] { "CampaignId", "CustomerExternalId" },
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseResults");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "RewardGrants");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "Campaigns");
        }
    }
}
