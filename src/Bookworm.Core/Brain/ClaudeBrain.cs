using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bookworm.Core.Brain.Tools;

namespace Bookworm.Core.Brain;

/// <summary>
/// Real Claude integration via a hand-rolled call to the Messages API — no official Anthropic .NET SDK
/// exists, and a small HttpClient call keeps Bookworm.Core dependency-free for this, which also helps
/// the future MAUI/Android port (see docs/decisions.md).
/// </summary>
public sealed class ClaudeBrain : IBrain, IDisposable
{
    // WhenWritingNull is essential here, not cosmetic: Claude's API validates each content block strictly
    // against its type ("text" blocks may not carry tool_use/tool_result fields at all), so ContentBlock's
    // unused nullable properties must be omitted entirely rather than serialized as explicit nulls.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _model;

    public ClaudeBrain(string apiKey, string model = "claude-sonnet-5")
    {
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/") };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<BrainTurnResult> RespondAsync(ConversationContext context, IReadOnlyList<ToolDefinition> tools, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 2048,
            system = context.SystemPrompt,
            messages = context.Messages,
            tools,
        };

        using var response = await _http.PostAsJsonAsync("v1/messages", requestBody, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Claude API request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<ClaudeMessageResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Claude API returned an empty response.");
        return new BrainTurnResult { Content = parsed.Content };
    }

    public void Dispose() => _http.Dispose();

    private sealed class ClaudeMessageResponse
    {
        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = [];

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
    }
}
