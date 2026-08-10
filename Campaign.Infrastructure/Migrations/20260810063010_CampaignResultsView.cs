using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Campaign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CampaignResultsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The counting lives here and only here: the endpoint reads this view, and SSRS or Power
            // BI can read the same one without anybody reimplementing the arithmetic.
            //
            // The grain is one agent on one business date. A grant has exactly one of each, so the
            // rows are disjoint and grouping by agent or by day is a matter of adding them up.
            //
            // P-09: a voided grant is in neither the numerator nor the denominator. ConvertedGrants
            // counts distinct grants rather than purchase rows, so a customer who bought three times
            // converts one grant. The left join fans a grant out over its purchases, which is why
            // every grant count is DISTINCT.
            //
            // MatchedRows is deliberately not filtered by the grant's status: it reports how many
            // imported rows were actually matched, and voiding a grant afterwards does not un-match a
            // purchase that already happened. A row imported after the grant was voided finds no
            // active grant and stays Unmatched, so it never reaches this count in the first place.
            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW vw_CampaignResults AS
                SELECT
                    g.CampaignId,
                    g.AgentId,
                    a.ExternalUserId AS AgentExternalUserId,
                    a.DisplayName    AS AgentDisplayName,
                    g.BusinessDate,
                    COUNT(DISTINCT CASE WHEN g.Status = 'Active' THEN g.Id END)             AS ActiveGrants,
                    COUNT(DISTINCT CASE WHEN g.Status = 'Voided' THEN g.Id END)             AS VoidedGrants,
                    COUNT(DISTINCT CASE WHEN g.Status = 'Active' THEN p.MatchedGrantId END) AS ConvertedGrants,
                    COUNT(CASE WHEN p.MatchStatus = 'Matched' THEN 1 END)                   AS MatchedRows
                FROM RewardGrants AS g
                INNER JOIN Agents AS a ON a.Id = g.AgentId
                LEFT JOIN PurchaseResults AS p ON p.MatchedGrantId = g.Id
                GROUP BY g.CampaignId, g.AgentId, a.ExternalUserId, a.DisplayName, g.BusinessDate;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS vw_CampaignResults;");
        }
    }
}
