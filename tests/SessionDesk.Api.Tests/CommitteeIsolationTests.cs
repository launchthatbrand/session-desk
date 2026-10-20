using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SessionDesk.Api.Briefing;
using Testcontainers.PostgreSql;

namespace SessionDesk.Api.Tests;

public sealed class FakeMiniMaxClient : IMiniMaxClient
{
    public string NextContent { get; set; } = "SB 1204 is on the February 12 agenda in room 412.";

    public Task<MiniMaxCompletion> CompleteAsync(string system, string user, CancellationToken cancellationToken) =>
        Task.FromResult(new MiniMaxCompletion(NextContent, 12, 24));
}

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public FakeMiniMaxClient MiniMax { get; } = new();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync() => await _postgres.DisposeAsync();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-should-be-32b-min");
        builder.UseSetting("Seed:AdminEmail", "alex.morgan@sessiondesk.local");
        builder.UseSetting("Seed:AdminPassword", "TestPassword!1");
        builder.UseSetting("MiniMax:ApiKey", "test-key");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(MiniMax);
            services.AddSingleton<IMiniMaxClient>(MiniMax);
        });
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

[Collection("api")]
public sealed class CommitteeIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Viewer_of_rules_cannot_read_appropriations()
    {
        var clerk = await Login("alex.morgan@sessiondesk.local");
        var appropriations = CommitteeId(clerk.Body, "Appropriations");
        var login = await Login("casey.nguyen@sessiondesk.local");
        using var request = Authorized(login, appropriations);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("/api/bills", UriKind.Relative);
        var response = await login.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Clerk_only_receives_bills_for_the_selected_committee()
    {
        var login = await Login("alex.morgan@sessiondesk.local");
        var appropriations = CommitteeId(login.Body, "Appropriations");
        var rules = CommitteeId(login.Body, "Rules");

        var appropriationBills = await GetBills(login, appropriations);
        var rulesBills = await GetBills(login, rules);

        Assert.Equal(4, appropriationBills.Count);
        Assert.DoesNotContain(appropriationBills, bill => bill.GetProperty("number").GetString() == "SB 1302");
        Assert.Single(rulesBills);
        Assert.Equal("SB 1302", rulesBills[0].GetProperty("number").GetString());
    }

    [Fact]
    public async Task Viewer_cannot_record_an_action()
    {
        var login = await Login("casey.nguyen@sessiondesk.local");
        var rules = CommitteeId(login.Body, "Rules");
        var bills = await GetBills(login, rules);
        var billId = bills[0].GetProperty("id").GetGuid();

        using var request = Authorized(login, rules);
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri($"/api/bills/{billId}/actions", UriKind.Relative);
        request.Content = JsonContent.Create(new { action = "bill.reported_favorably" });
        var response = await login.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_request_a_brief()
    {
        var login = await Login("casey.nguyen@sessiondesk.local");
        var rules = CommitteeId(login.Body, "Rules");
        var bills = await GetBills(login, rules);
        var response = await Send(login, rules, HttpMethod.Post, $"/api/bills/{bills[0].GetProperty("id").GetGuid()}/brief");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Draft_that_cites_an_outside_bill_is_withheld()
    {
        factory.MiniMax.NextContent = "SB 9999 needs a closer look.";
        var login = await Login("alex.morgan@sessiondesk.local");
        var appropriations = CommitteeId(login.Body, "Appropriations");
        var billId = await BillId(login, appropriations, "SB 1204");
        var before = await GetBills(login, appropriations);
        var response = await Send(login, appropriations, HttpMethod.Post, $"/api/bills/{billId}/brief");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SB 9999 needs", body);
        var after = await GetBills(login, appropriations);
        Assert.Equal(
            before.Single(bill => bill.GetProperty("number").GetString() == "SB 1204").GetProperty("lastAction").GetString(),
            after.Single(bill => bill.GetProperty("number").GetString() == "SB 1204").GetProperty("lastAction").GetString());
        var audit = await GetAudit(login, appropriations);
        Assert.Contains(audit, item => item.GetProperty("action").GetString() == "ai.draft_withheld");
    }

    [Fact]
    public async Task Accepted_brief_becomes_a_staff_note()
    {
        factory.MiniMax.NextContent = "SB 1204 is on the February 12 agenda in room 412.";
        var login = await Login("alex.morgan@sessiondesk.local");
        var appropriations = CommitteeId(login.Body, "Appropriations");
        var billId = await BillId(login, appropriations, "SB 1204");
        var response = await Send(login, appropriations, HttpMethod.Post, $"/api/bills/{billId}/brief");
        response.EnsureSuccessStatusCode();
        var brief = await response.Content.ReadFromJsonAsync<JsonElement>();
        var briefingId = brief.GetProperty("briefingId").GetGuid();

        var decision = await Send(
            login,
            appropriations,
            HttpMethod.Post,
            $"/api/bills/{billId}/brief/{briefingId}",
            new { decision = "accept", note = "SB 1204 is on the February 12 agenda in room 412." });
        decision.EnsureSuccessStatusCode();

        var audit = await GetAudit(login, appropriations);
        var note = audit.Single(item => item.GetProperty("action").GetString() == "bill.note_added" && item.GetProperty("traceId").GetString() == briefingId.ToString());
        Assert.Contains(briefingId.ToString(), note.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Discarded_brief_leaves_the_bill_unchanged()
    {
        factory.MiniMax.NextContent = "SB 1210 asks cities to report cyber incidents.";
        var login = await Login("alex.morgan@sessiondesk.local");
        var appropriations = CommitteeId(login.Body, "Appropriations");
        var billId = await BillId(login, appropriations, "SB 1210");
        var before = (await GetBills(login, appropriations)).Single(bill => bill.GetProperty("id").GetGuid() == billId);
        var response = await Send(login, appropriations, HttpMethod.Post, $"/api/bills/{billId}/brief");
        response.EnsureSuccessStatusCode();
        var briefingId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("briefingId").GetGuid();

        var decision = await Send(login, appropriations, HttpMethod.Post, $"/api/bills/{billId}/brief/{briefingId}", new { decision = "discard" });
        decision.EnsureSuccessStatusCode();

        var after = (await GetBills(login, appropriations)).Single(bill => bill.GetProperty("id").GetGuid() == billId);
        Assert.Equal(before.GetProperty("lastAction").GetString(), after.GetProperty("lastAction").GetString());
        var audit = await GetAudit(login, appropriations);
        Assert.Contains(audit, item => item.GetProperty("action").GetString() == "ai.draft_discarded" && item.GetProperty("traceId").GetString() == briefingId.ToString());
        Assert.DoesNotContain(audit, item => item.GetProperty("action").GetString() == "bill.note_added" && item.GetProperty("traceId").GetString() == briefingId.ToString());
    }

    private async Task<LoginSession> Login(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "TestPassword!1" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new LoginSession(client, body);
    }

    private static Guid CommitteeId(JsonElement login, string name)
    {
        foreach (var committee in login.GetProperty("committees").EnumerateArray())
        {
            if (committee.GetProperty("name").GetString() == name)
            {
                return committee.GetProperty("id").GetGuid();
            }
        }

        throw new InvalidOperationException($"Missing committee {name}");
    }

    private static async Task<Guid> BillId(LoginSession login, Guid committeeId, string number)
    {
        var bills = await GetBills(login, committeeId);
        return bills.Single(bill => bill.GetProperty("number").GetString() == number).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> GetAudit(LoginSession login, Guid committeeId)
    {
        var response = await Send(login, committeeId, HttpMethod.Get, "/api/audit");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JsonElement>>() ?? [];
    }

    private static async Task<HttpResponseMessage> Send(LoginSession login, Guid committeeId, HttpMethod method, string path, object? body = null)
    {
        using var request = Authorized(login, committeeId);
        request.Method = method;
        request.RequestUri = new Uri(path, UriKind.Relative);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await login.Client.SendAsync(request);
    }

    private static async Task<List<JsonElement>> GetBills(LoginSession login, Guid committeeId)
    {
        using var request = Authorized(login, committeeId);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("/api/bills", UriKind.Relative);
        var response = await login.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        return body ?? [];
    }

    private static HttpRequestMessage Authorized(LoginSession login, Guid committeeId)
    {
        var request = new HttpRequestMessage();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Body.GetProperty("token").GetString());
        request.Headers.Add("X-Committee-Id", committeeId.ToString());
        return request;
    }

    private sealed record LoginSession(HttpClient Client, JsonElement Body);
}
