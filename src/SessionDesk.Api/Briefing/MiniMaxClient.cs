using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SessionDesk.Api.Briefing;

public interface IMiniMaxClient
{
    Task<MiniMaxCompletion> CompleteAsync(string system, string user, CancellationToken cancellationToken);
}

public sealed record MiniMaxCompletion(string Content, int? InputTokens, int? OutputTokens);

public sealed class MiniMaxClient(HttpClient http, IConfiguration configuration) : IMiniMaxClient
{
    public const string ModelName = "MiniMax-M3.1-Flash";

    public async Task<MiniMaxCompletion> CompleteAsync(string system, string user, CancellationToken cancellationToken)
    {
        var key = configuration["MiniMax:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("MiniMax:ApiKey is required.");
        }

        var payload = new
        {
            model = ModelName,
            reasoning_effort = "low",
            max_completion_tokens = 600,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.minimax.io/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var message = body.GetProperty("choices")[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString() ?? ""
            : "";
        int? input = null;
        int? output = null;
        if (body.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            {
                input = promptTokens.GetInt32();
            }

            if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            {
                output = completionTokens.GetInt32();
            }
        }

        return new MiniMaxCompletion(content.Trim(), input, output);
    }
}
