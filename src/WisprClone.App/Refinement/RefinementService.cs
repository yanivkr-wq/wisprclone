using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace WisprClone.App.Refinement;

/// <summary>
/// Calls OpenAI's Chat Completion API to rewrite a transcribed snippet in a
/// requested style. Preserves the original language (Hebrew or English) and
/// returns just the rewritten text — no preamble, no quotes, no labels.
///
/// Cost on gpt-4o-mini: roughly $0.0001 per refinement (≈30 input + 30 output
/// tokens for a typical sentence).
/// </summary>
public sealed class RefinementService : IDisposable
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";

    private readonly HttpClient _http;
    private readonly string _model;

    public RefinementService(string apiKey, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));

        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WisprClone/0.1");

        Log.Information("RefinementService ready (model={Model})", _model);
    }

    /// <summary>Swap the API key without recreating the service.</summary>
    public void UpdateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(nameof(apiKey));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> RefineAsync(string originalText, RefinementStyle style, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(originalText)) return string.Empty;

        var systemPrompt = BuildSystemPrompt(style);

        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = originalText }
            },
            temperature = 0.7,
            max_tokens = 600
        };

        using var response = await _http.PostAsJsonAsync(Endpoint, requestBody, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("OpenAI chat API {Status}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"OpenAI chat API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var json = JsonDocument.Parse(body);
        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return content.Trim().Trim('"');
    }

    private static string BuildSystemPrompt(RefinementStyle style)
    {
        const string shared = "Preserve the original language (Hebrew, English, or whatever the input is in). " +
                              "Preserve the original intent and meaning. " +
                              "Return ONLY the rewritten text — no preamble, no quotation marks, no commentary.";

        return style switch
        {
            RefinementStyle.Professional =>
                "You are a writing assistant. Rewrite the user's text to sound more professional and polished, " +
                "suitable for business communication. Fix grammar and word choice. " + shared,
            RefinementStyle.Casual =>
                "You are a writing assistant. Rewrite the user's text in a casual, friendly, conversational tone. " +
                "Use contractions where natural. " + shared,
            RefinementStyle.Shorter =>
                "You are a writing assistant. Condense the user's text. Make it as short as possible while " +
                "keeping it natural and not telegraphic. " + shared,
            RefinementStyle.Longer =>
                "You are a writing assistant. Expand the user's text with useful detail and natural elaboration. " +
                "Don't pad with filler — only add content that makes the message clearer or more complete. " + shared,
            _ => "Rewrite the user's text. " + shared
        };
    }

    public void Dispose() => _http.Dispose();
}
