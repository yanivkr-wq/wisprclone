using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace WisprClone.App.Transcription;

/// <summary>
/// Cloud transcription via OpenAI's Whisper API (model: whisper-1).
/// Multilingual auto-detect — handles Hebrew + English without configuration.
/// Same Whisper model you'd run locally, on GPU servers → 1-3s latency.
/// Cost: ~$0.006/min of audio (≈$5-10/month for heavy dictation).
/// </summary>
public sealed class WhisperEngine : IDisposable
{
    private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
    private const string Model = "whisper-1";

    private readonly HttpClient _http;

    public WhisperEngine(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WisprClone/0.1");

        Log.Information("WhisperEngine: cloud mode (OpenAI {Model})", Model);
    }

    /// <summary>
    /// Swaps the API key in the existing HttpClient. Lets the settings window
    /// apply a new key without recreating the engine + tearing down the
    /// pipeline.
    /// </summary>
    public void UpdateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        Log.Information("WhisperEngine API key updated");
    }

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        // Open the file outside any using — the MultipartFormDataContent disposes
        // its children (StreamContent) which in turn disposes the underlying stream.
        var fileStream = File.OpenRead(wavPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        using var form = new MultipartFormDataContent
        {
            { fileContent, "file", Path.GetFileName(wavPath) },
            { new StringContent(Model), "model" },
            { new StringContent("json"), "response_format" },
            // No "language" hint — let the server auto-detect so mixed
            // Hebrew + English clips return text in the dominant script.
        };

        using var response = await _http.PostAsync(Endpoint, form, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("OpenAI API {Status}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"OpenAI Whisper API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("text").GetString()?.Trim() ?? string.Empty;
    }

    public void Dispose() => _http.Dispose();
}
