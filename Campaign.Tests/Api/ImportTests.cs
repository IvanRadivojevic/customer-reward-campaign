namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Campaign.Api.Auth;
using Campaign.Api.Errors;
using Campaign.Core.Domain;
using Campaign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The purchase report end to end: a dirty file is processed to the last row, the same file twice is
/// still one batch, and the answer is always 200 with the summary.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ImportTests
{
    private const string CleanFile = """
        CustomerId,PurchaseDate,Amount,Currency
        1,2026-09-14,149.90,EUR
        1,2026-09-21,19.99,EUR
        1,2026-10-02,75.40,EUR
        2,2026-09-15,89.00,EUR
        """;

    private const string DirtyFile = """
        CustomerId;PurchaseDate;Amount;Currency
        1;2026-09-14;149.90;EUR
        2;14.09.2026;89.00;EUR
        ;2026-09-15;240.50;EUR
        3;2026-09-16;;
        4;2026-09-16;55.00;
        9999;2026-09-17;12.00;EUR
        5;not-a-date;33.00;EUR
        6;2026-09-18;abc;EUR
        7;2026-09-19;;EUR
        8;2026-09-20;61.00;EURO
        """;

    /// <summary>
    /// A fresh integration account per test. The import limit is ten a minute and it is counted per
    /// token, so tests that shared an account would refuse each other rather than test anything.
    /// </summary>
    private readonly string _integration = $"integration-{Guid.NewGuid():N}";

    private readonly ApiFixture _fixture;

    public ImportTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_dirty_file_is_processed_to_the_end_and_every_row_gets_a_status()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");
        await GrantTo(world, "3");

        var response = await UploadAsync(world.CampaignId, DirtyFile, "purchases-dirty.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var batch = await ReadBatchAsync(response);

        // Ten data rows, and the three outcomes have to account for all of them.
        Assert.Equal(10, batch.RowsTotal);
        Assert.Equal(batch.RowsTotal, batch.RowsMatched + batch.RowsUnmatched + batch.RowsInvalid);

        // Two rows are readable and rewarded, one is readable but unknown, the rest cannot be read.
        Assert.Equal(2, batch.RowsMatched);
        Assert.Equal(1, batch.RowsUnmatched);
        Assert.Equal(7, batch.RowsInvalid);
        Assert.Equal(nameof(ImportBatchStatus.CompletedWithErrors), batch.Status);
    }

    [Fact]
    public async Task A_customer_who_bought_three_times_gets_three_matched_rows()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");

        var response = await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv");
        var batch = await ReadBatchAsync(response);

        // Without an order identifier in the file, a repeated customer is a repeated purchase.
        Assert.Equal(4, batch.RowsTotal);
        Assert.Equal(3, batch.RowsMatched);
        Assert.Equal(1, batch.RowsUnmatched);
        Assert.Equal(0, batch.RowsInvalid);
        Assert.Equal(nameof(ImportBatchStatus.Completed), batch.Status);
    }

    [Fact]
    public async Task An_unmatched_row_is_reported_rather_than_dropped()
    {
        var world = await _fixture.NewWorldAsync();

        var upload = await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv");
        var batch = await ReadBatchAsync(upload);

        var detail = await _fixture.Client.SendAsync(
            ApiFixture.Get($"/api/v1/imports/{batch.Id}", _integration, CampaignRoles.Integration));

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var rows = await ReadRowsAsync(detail);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(nameof(MatchStatus.Unmatched), row.MatchStatus));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.RawLine)));
    }

    [Fact]
    public async Task An_invalid_row_keeps_its_raw_line_and_its_reason_and_no_parsed_field()
    {
        var world = await _fixture.NewWorldAsync();

        var upload = await UploadAsync(world.CampaignId, DirtyFile, "purchases-dirty.csv");
        var batch = await ReadBatchAsync(upload);

        var detail = await _fixture.Client.SendAsync(
            ApiFixture.Get($"/api/v1/imports/{batch.Id}", _integration, CampaignRoles.Integration));

        var invalid = (await ReadRowsAsync(detail))
            .Where(row => row.MatchStatus == nameof(MatchStatus.Invalid))
            .ToList();

        Assert.NotEmpty(invalid);
        Assert.All(invalid, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Error));
            Assert.False(string.IsNullOrWhiteSpace(row.RawLine));

            // A value taken out of a broken row is not data anybody should trust.
            Assert.Null(row.CustomerExternalId);
            Assert.Null(row.PurchaseDate);
            Assert.Null(row.Amount);
            Assert.Null(row.Currency);
            Assert.Null(row.MatchedGrantId);
        });
    }

    [Fact]
    public async Task P08_the_same_file_sent_twice_makes_one_batch()
    {
        var world = await _fixture.NewWorldAsync();

        var first = await ReadBatchAsync(await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv"));
        var second = await ReadBatchAsync(await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv"));

        Assert.Equal(first.Id, second.Id);
        Assert.False(first.AlreadyImported);
        Assert.True(second.AlreadyImported);
        Assert.Equal(1, await CountBatchesAsync(world.CampaignId));
    }

    [Fact]
    public async Task The_answer_is_always_200_and_never_202()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_file_that_is_not_a_purchase_report_is_refused_as_csv_invalid()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await UploadAsync(world.CampaignId, "Nothing;To;See\n1;2;3", "purchases.csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DomainErrorCodes.CsvInvalid, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task A_file_that_is_not_a_csv_at_all_is_refused_before_it_is_read()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await UploadAsync(world.CampaignId, CleanFile, "purchases.xlsx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DomainErrorCodes.CsvInvalid, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_unknown_batch_is_reported_with_its_own_type()
    {
        var response = await _fixture.Client.SendAsync(
            ApiFixture.Get($"/api/v1/imports/{Guid.NewGuid()}", _integration, CampaignRoles.Integration));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DomainErrorCodes.ImportBatchNotFound, await ApiFixture.ProblemTypeAsync(response));
    }

    [Fact]
    public async Task An_agent_may_not_import()
    {
        var world = await _fixture.NewWorldAsync();

        var response = await UploadAsync(
            world.CampaignId,
            CleanFile,
            "purchases-clean.csv",
            world.AgentSubject,
            CampaignRoles.Agent);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_row_that_no_column_could_hold_is_invalid_and_the_rest_of_the_file_still_lands()
    {
        var world = await _fixture.NewWorldAsync();
        await GrantTo(world, "1");

        // Each of these three passes the parser but cannot be stored: an id longer than the column,
        // a number larger than decimal(18,2), and a field long enough to blow past the 1000
        // characters an error message gets. Letting any of them reach the insert would fail the whole
        // batch and lose the good rows with it.
        var poisoned = string.Join(
            '\n',
            "CustomerId,PurchaseDate,Amount,Currency",
            "1,2026-09-14,149.90,EUR",
            $"{new string('9', 200)},2026-09-15,10.00,EUR",
            "2,2026-09-16,99999999999999999999.99,EUR",
            $"3,2026-09-17,{new string('7', 3000)},EUR",
            "4,2026-09-18,25.00,EUR");

        var response = await UploadAsync(world.CampaignId, poisoned, "purchases-poisoned.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var batch = await ReadBatchAsync(response);

        Assert.Equal(5, batch.RowsTotal);
        Assert.Equal(batch.RowsTotal, batch.RowsMatched + batch.RowsUnmatched + batch.RowsInvalid);
        Assert.Equal(3, batch.RowsInvalid);

        // The two readable rows survived: one rewarded customer and one who was not.
        Assert.Equal(1, batch.RowsMatched);
        Assert.Equal(1, batch.RowsUnmatched);

        var detail = await _fixture.Client.SendAsync(
            ApiFixture.Get($"/api/v1/imports/{batch.Id}", _integration, CampaignRoles.Integration));

        var invalid = (await ReadRowsAsync(detail))
            .Where(row => row.MatchStatus == nameof(MatchStatus.Invalid))
            .ToList();

        Assert.Equal(3, invalid.Count);
        Assert.All(invalid, row =>
        {
            Assert.NotNull(row.Error);
            Assert.InRange(row.Error!.Length, 1, PurchaseResult.MaxErrorLength);
        });
    }

    [Fact]
    public async Task The_eleventh_upload_in_a_minute_is_refused()
    {
        var world = await _fixture.NewWorldAsync();

        // Ten a minute on this endpoint, tighter than the hundred every other endpoint gets, because
        // an import is expensive and nobody sends ten purchase reports a minute by hand.
        for (var upload = 1; upload <= 10; upload++)
        {
            var allowed = await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var refused = await UploadAsync(world.CampaignId, CleanFile, "purchases-clean.csv");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiErrorCodes.RateLimitExceeded, await ApiFixture.ProblemTypeAsync(refused));
        Assert.Equal(60, refused.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    private async Task<HttpResponseMessage> UploadAsync(
        Guid campaignId,
        string csv,
        string fileName,
        string? subject = null,
        string role = CampaignRoles.Integration)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/campaigns/{campaignId}/imports")
        {
            Content = content
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiFixture.TokenFor(subject ?? _integration, role));

        return await _fixture.Client.SendAsync(request);
    }

    private async Task GrantTo(TestWorld world, string customerExternalId) =>
        await _fixture.Client.SendAsync(ApiFixture.GrantRequest(
            world.CampaignId,
            customerExternalId,
            world.AgentSubject,
            Guid.NewGuid().ToString()));

    private async Task<int> CountBatchesAsync(Guid campaignId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ImportBatches.CountAsync(batch => batch.CampaignId == campaignId);
    }

    private static async Task<BatchBody> ReadBatchAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<BatchBody>()
        ?? throw new InvalidOperationException("The upload returned no body.");

    private static async Task<IReadOnlyList<RowBody>> ReadRowsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<DetailBody>())?.Rows
        ?? throw new InvalidOperationException("The batch returned no rows.");

    private sealed record BatchBody(
        Guid Id,
        int RowsTotal,
        int RowsMatched,
        int RowsUnmatched,
        int RowsInvalid,
        string Status,
        bool AlreadyImported);

    private sealed record RowBody(
        int RowNumber,
        string MatchStatus,
        string? CustomerExternalId,
        DateOnly? PurchaseDate,
        decimal? Amount,
        string? Currency,
        Guid? MatchedGrantId,
        string? Error,
        string RawLine);

    private sealed record DetailBody(BatchBody Batch, IReadOnlyList<RowBody> Rows);
}
