using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Meetily.Core.Models;
using Meetily.Core.Services;

namespace Meetily.AI;

public class OllamaSummarizationService : ISummarizationService
{
    private readonly HttpClient _httpClient;

    public OllamaSummarizationService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<bool> TestConnectionAsync(string endpoint)
    {
        try
        {
            var baseUri = endpoint.TrimEnd('/');
            using var response = await _httpClient.GetAsync($"{baseUri}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListAvailableModelsAsync(string endpoint)
    {
        var models = new List<string>();
        try
        {
            var baseUri = endpoint.TrimEnd('/');
            var response = await _httpClient.GetStringAsync($"{baseUri}/api/tags");
            var json = JsonNode.Parse(response);
            if (json?["models"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item?["name"]?.GetValue<string>() is string name)
                    {
                        models.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ollama] Failed to list models: {ex.Message}");
        }

        if (models.Count == 0)
        {
            models.Add("llama3:latest");
            models.Add("mistral:latest");
            models.Add("phi3:latest");
        }

        return models;
    }

    public async Task<SummaryResult> GenerateSummaryAsync(string endpoint, string model, string fullTranscript, CancellationToken cancellationToken = default)
    {
        var prompt = $@"
You are an executive meeting assistant. Analyze the following meeting transcript and provide a structured summary in JSON format.

JSON format required:
{{
  ""summary"": ""A clear, concise 2-3 paragraph executive summary of the meeting."",
  ""actionItems"": [""Action item 1 with owner if mentioned"", ""Action item 2""],
  ""keyTopics"": [""Topic 1"", ""Topic 2""],
  ""decisions"": [""Decision 1"", ""Decision 2""]
}}

Only respond with the valid JSON object. Do not add markdown backticks or commentary.

Meeting Transcript:
{fullTranscript}
";

        var baseUri = endpoint.TrimEnd('/');
        var requestBody = new
        {
            model = model,
            prompt = prompt,
            stream = false,
            format = "json"
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{baseUri}/api/generate", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        var jsonResponse = JsonNode.Parse(responseString);
        var responseText = jsonResponse?["response"]?.GetValue<string>() ?? "{}";

        try
        {
            var parsed = JsonSerializer.Deserialize<SummaryResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Summary))
            {
                return parsed;
            }
        }
        catch
        {
            // Fallback if model output isn't pure JSON
        }

        return new SummaryResult
        {
            Summary = responseText,
            ActionItems = new List<string> { "Review generated transcript and summary." }
        };
    }
}
