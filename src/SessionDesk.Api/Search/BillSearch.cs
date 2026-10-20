using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SessionDesk.Api.Data;

namespace SessionDesk.Api.Search;

public interface IBillSearch
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string term, CancellationToken cancellationToken);
}

public sealed record SearchHit(
    Guid BillId,
    string Number,
    string Title,
    string Sponsor,
    Guid CommitteeId,
    string Committee,
    string Citation,
    string Snippet);

public sealed record SearchBill(
    Guid BillId,
    string Number,
    string Title,
    string Sponsor,
    Guid CommitteeId,
    string Committee,
    string Citation,
    string Body);

public sealed class OpenSearchBillSearch(HttpClient http, IConfiguration configuration, AppDbContext db) : IBillSearch
{
    public const string Index = "session-bills";

    public async Task EnsureIndexedAsync(CancellationToken cancellationToken)
    {
        var url = configuration["OpenSearch:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        using var head = await http.GetAsync($"{url.TrimEnd('/')}/{Index}", cancellationToken);
        if (head.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var mapping = new
            {
                settings = new { number_of_shards = 1, number_of_replicas = 0 },
                mappings = new
                {
                    properties = new
                    {
                        billId = new { type = "keyword" },
                        number = new { type = "keyword" },
                        title = new { type = "text" },
                        sponsor = new { type = "text" },
                        committeeId = new { type = "keyword" },
                        committee = new { type = "keyword" },
                        status = new { type = "keyword" },
                        citation = new { type = "keyword" },
                        body = new { type = "text" },
                    },
                },
            };
            using var created = await http.PutAsJsonAsync($"{url.TrimEnd('/')}/{Index}", mapping, cancellationToken);
            created.EnsureSuccessStatusCode();
        }
        else
        {
            head.EnsureSuccessStatusCode();
        }

        var count = await http.GetFromJsonAsync<JsonElement>($"{url.TrimEnd('/')}/{Index}/_count", cancellationToken);
        if (count.GetProperty("count").GetInt32() > 0)
        {
            return;
        }

        var bills = await db.Bills.AsNoTracking()
            .Select(bill => new
            {
                bill.Id,
                bill.Number,
                bill.Title,
                bill.Sponsor,
                bill.CommitteeId,
                Committee = bill.Committee.Name,
                bill.Status,
                bill.Citation,
                bill.Body,
            })
            .ToListAsync(cancellationToken);
        var payload = new StringBuilder();
        foreach (var bill in bills)
        {
            payload.Append("{\"index\":{\"_id\":\"").Append(bill.Id).Append("\"}}\n");
            payload.Append(JsonSerializer.Serialize(new
            {
                billId = bill.Id,
                number = bill.Number,
                title = bill.Title,
                sponsor = bill.Sponsor,
                committeeId = bill.CommitteeId,
                committee = bill.Committee,
                status = bill.Status,
                citation = bill.Citation,
                body = bill.Body,
            }));
            payload.Append('\n');
        }

        using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/x-ndjson");
        using var bulk = await http.PostAsync($"{url.TrimEnd('/')}/{Index}/_bulk?refresh=true", content, cancellationToken);
        bulk.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, CancellationToken cancellationToken)
    {
        var url = configuration["OpenSearch:Url"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var should = new List<object>
        {
            new { multi_match = new { query = term, fields = new[] { "title^3", "body", "sponsor", "citation" } } },
        };
        if (LooksLikeNumber(term))
        {
            var fragment = Regex.Replace(term, "[^0-9A-Za-z]", "");
            should.Add(new { wildcard = new { number = new { value = $"*{fragment}*", case_insensitive = true } } });
        }

        var query = new
        {
            size = 25,
            query = new { @bool = new { should, minimum_should_match = 1 } },
            highlight = new { fields = new { body = new { }, title = new { }, citation = new { } } },
        };
        using var response = await http.PostAsJsonAsync($"{url.TrimEnd('/')}/{Index}/_search", query, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var hits = new List<SearchHit>();
        foreach (var hit in body.GetProperty("hits").GetProperty("hits").EnumerateArray())
        {
            var source = hit.GetProperty("_source");
            hits.Add(new SearchHit(
                source.GetProperty("billId").GetGuid(),
                source.GetProperty("number").GetString() ?? "",
                source.GetProperty("title").GetString() ?? "",
                source.GetProperty("sponsor").GetString() ?? "",
                source.GetProperty("committeeId").GetGuid(),
                source.GetProperty("committee").GetString() ?? "",
                source.GetProperty("citation").GetString() ?? "",
                Snippet(hit)));
        }

        return hits;
    }

    private static bool LooksLikeNumber(string term) =>
        Regex.IsMatch(term, @"\d") || Regex.IsMatch(term, @"\b(SB|HB)\b", RegexOptions.IgnoreCase);

    private static string Snippet(JsonElement hit)
    {
        if (!hit.TryGetProperty("highlight", out var highlight))
        {
            return "";
        }

        foreach (var field in new[] { "body", "title", "citation" })
        {
            if (highlight.TryGetProperty(field, out var values) && values.GetArrayLength() > 0)
            {
                return values[0].GetString() ?? "";
            }
        }

        return "";
    }
}

public sealed class BillIndexService(IServiceScopeFactory scopes, ILogger<BillIndexService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 60 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var search = scope.ServiceProvider.GetRequiredService<OpenSearchBillSearch>();
                await search.EnsureIndexedAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (attempt < 60)
            {
                logger.LogWarning(ex, "Bill index is not ready");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
