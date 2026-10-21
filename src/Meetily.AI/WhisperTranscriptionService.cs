using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Meetily.Core.Models;
using Meetily.Core.Services;

namespace Meetily.AI;

public class WhisperTranscriptionService : ITranscriptionService
{
    private readonly string _modelsDir;
    private readonly HttpClient _httpClient;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<TranscriptSegment>? SegmentDiscovered;

    public WhisperTranscriptionService(string? modelsDirectory = null)
    {
        _modelsDir = modelsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
        _httpClient = new HttpClient();
        Directory.CreateDirectory(_modelsDir);
    }

    public string GetModelPath(string modelName)
    {
        var cleanName = modelName.ToLowerInvariant().Replace("ggml-", "").Replace(".bin", "");
        return Path.Combine(_modelsDir, $"ggml-{cleanName}.bin");
    }

    public Task<bool> IsModelDownloadedAsync(string modelName)
    {
        var path = GetModelPath(modelName);
        return Task.FromResult(File.Exists(path) && new FileInfo(path).Length > 10_000_000);
    }

    public async Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken cancellationToken = default)
    {
        var targetPath = GetModelPath(modelName);
        var cleanName = modelName.ToLowerInvariant().Replace("ggml-", "").Replace(".bin", "");

        // Determine GgmlType
        var ggmlType = cleanName switch
        {
            "tiny" => GgmlType.Tiny,
            "base" => GgmlType.Base,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large" => GgmlType.LargeV3,
            _ => GgmlType.Base
        };

        StatusChanged?.Invoke(this, $"Downloading Whisper model '{cleanName}'...");

        using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken);
        using var fileWriter = File.Create(targetPath);

        byte[] buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await modelStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileWriter.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalBytesRead += bytesRead;
            progress?.Report(totalBytesRead / (1024f * 1024f)); // Reporting MBs downloaded
        }

        StatusChanged?.Invoke(this, $"Model '{cleanName}' ready.");
    }

    public async Task<List<TranscriptSegment>> TranscribeFileAsync(string audioFilePath, string modelName, string language = "auto", CancellationToken cancellationToken = default)
    {
        var modelPath = GetModelPath(modelName);
        if (!File.Exists(modelPath))
        {
            await DownloadModelAsync(modelName, null, cancellationToken);
        }

        StatusChanged?.Invoke(this, "Initializing transcription engine...");

        var segments = new List<TranscriptSegment>();

        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        var builder = whisperFactory.CreateBuilder();

        if (language != "auto" && !string.IsNullOrWhiteSpace(language))
        {
            builder.WithLanguage(language);
        }

        using var processor = builder.Build();

        StatusChanged?.Invoke(this, "Transcribing audio...");
        using var fileStream = File.OpenRead(audioFilePath);

        await foreach (var segmentData in processor.ProcessAsync(fileStream, cancellationToken))
        {
            var segment = new TranscriptSegment
            {
                StartSeconds = segmentData.Start.TotalSeconds,
                EndSeconds = segmentData.End.TotalSeconds,
                Text = segmentData.Text.Trim(),
                Confidence = segmentData.Probability
            };

            segments.Add(segment);
            SegmentDiscovered?.Invoke(this, segment);
        }

        StatusChanged?.Invoke(this, "Transcription completed.");
        return segments;
    }
}
