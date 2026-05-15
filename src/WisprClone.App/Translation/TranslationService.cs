using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace WisprClone.App.Translation;

/// <summary>
/// Translates arbitrary clipboard text via OpenAI's Chat Completion API.
/// Supports an explicit target language (e.g. "Hebrew", "English") or
/// "auto" — which flips between Hebrew and English based on whether the
/// input is dominated by Hebrew or Latin characters.
///
/// Cost on gpt-4o-mini is roughly $0.0001 per typical sentence.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private const string Model = "gpt-4o-mini";

    private readonly HttpClient _http;

    public TranslationService(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WisprClone/0.3");

        Log.Information("TranslationService ready (model={Model})", Model);
    }

    public void UpdateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(nameof(apiKey));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Translates <paramref name="text"/>. When <paramref name="targetLanguage"/>
    /// is "auto" the target is chosen by majority-character heuristic:
    /// Hebrew-dominant input → English target, otherwise → Hebrew target.
    /// Returns the translation; empty input → empty output.
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, "(empty)", "(empty)");

        var (resolvedTarget, sourceHint) = ResolveTarget(text, targetLanguage);

        var systemPrompt =
            $"Translate the user's text into {resolvedTarget}. " +
             "Preserve meaning, tone, formality and punctuation. " +
             "If the source language already IS the target language, return the input unchanged. " +
             "Return ONLY the translation — no preamble, no language labels, no quotation marks, no commentary.";

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = text }
            },
            temperature = 0.3,
            max_tokens = 1500
        };

        using var response = await _http.PostAsJsonAsync(Endpoint, requestBody, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("OpenAI chat API {Status} (translate): {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"OpenAI chat API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var json = JsonDocument.Parse(body);
        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return new TranslationResult(content.Trim().Trim('"'), sourceHint, resolvedTarget);
    }

    /// <summary>
    /// Resolves "auto" to a concrete target by inspecting the input's
    /// character composition. Hebrew-dominant → English, otherwise → Hebrew.
    /// Explicit targets are returned unchanged.
    /// </summary>
    private static (string target, string sourceHint) ResolveTarget(string text, string target)
    {
        if (!string.Equals(target, "auto", StringComparison.OrdinalIgnoreCase))
            return (target, DetectSourceHint(text));

        int hebrew = text.Count(c => c >= 0x0590 && c <= 0x05FF);
        int latin  = text.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));

        if (hebrew > latin)
            return ("English", "Hebrew");
        else
            return ("Hebrew", "English");
    }

    private static string DetectSourceHint(string text)
    {
        int hebrew = text.Count(c => c >= 0x0590 && c <= 0x05FF);
        int latin  = text.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        return hebrew > latin ? "Hebrew" : "English";
    }

    public void Dispose() => _http.Dispose();
}

public sealed record TranslationResult(string Text, string SourceHint, string TargetLanguage);
