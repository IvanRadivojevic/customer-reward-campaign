namespace Campaign.Tests.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Campaign.Api.Auth;
using Campaign.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// P-08 under load. The rule is settled by attempting the insert and reading the winner back, so this
/// test is the one that would catch a "does it exist yet" check: ten uploads arriving together would
/// all find nothing and all write.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ParallelImportTests
{
    /// <summary>
    /// Ten, not twelve: the agreement asks for at least ten simultaneous requests and the import
    /// limit allows ten per minute, so ten is the number that satisfies both.
    /// </summary>
    private const int Concurrency = 10;

    private const string File = """
        CustomerId,PurchaseDate,Amount,Currency
        1,2026-09-14,149.90,EUR
        2,2026-09-15,89.00,EUR
        """;

    /// <summary>Its own account, so this test has the whole per-token import budget to itself.</summary>
    private readonly string _integration = $"integration-{Guid.NewGuid():N}";

    private readonly ApiFixture _fixture;

    public ParallelImportTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task P08_parallel_uploads_of_one_file_make_one_batch_and_none_of_them_fails()
    {
        var world = await _fixture.NewWorldAsync();

        var responses = await Task.WhenAll(Enumerable
            .Range(0, Concurrency)
            .Select(_ => UploadAsync(world.CampaignId)));

        // Not one of them may fail: a repeated file is an answer, not an error.
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var batches = new List<BatchBody>();
        foreach (var response in responses)
        {
            batches.Add(await response.Content.ReadFromJsonAsync<BatchBody>()
                ?? throw new InvalidOperationException("The upload returned no body."));
        }

        // Every answer names the same batch, and exactly one of them made it.
        Assert.Single(batches.Select(batch => batch.Id).Distinct());
        Assert.Equal(1, batches.Count(batch => !batch.AlreadyImported));
        Assert.Equal(Concurrency - 1, batches.Count(batch => batch.AlreadyImported));
        Assert.Equal(1, await CountBatchesAsync(world.CampaignId));
    }

    private async Task<HttpResponseMessage> UploadAsync(Guid campaignId)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(File));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "purchases.csv");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/campaigns/{campaignId}/imports")
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiFixture.TokenFor(_integration, CampaignRoles.Integration));

        return await _fixture.Client.SendAsync(request);
    }

    private async Task<int> CountBatchesAsync(Guid campaignId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ImportBatches.CountAsync(batch => batch.CampaignId == campaignId);
    }

    private sealed record BatchBody(Guid Id, bool AlreadyImported);
}
