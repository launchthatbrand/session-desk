using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace SessionDesk.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync() => await _postgres.DisposeAsync();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-should-be-32b-min");
        builder.UseSetting("Seed:AdminEmail", "alex.morgan@sessiondesk.local");
        builder.UseSetting("Seed:AdminPassword", "TestPassword!1");
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
