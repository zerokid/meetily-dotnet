using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Meetily.Core.Models;
using Meetily.Core.Services;

namespace Meetily.App;

public class WebBridge
{
    private readonly IDatabaseService _db;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ISummarizationService _summarizationService;
    private WebView2? _webView;

    private string? _activeMeetingId;
    private DateTime _recordingStartTime;

    public WebBridge(
        IDatabaseService db,
        IAudioRecorder audioRecorder,
        ITranscriptionService transcriptionService,
        ISummarizationService summarizationService)
    {
        _db = db;
        _audioRecorder = audioRecorder;
        _transcriptionService = transcriptionService;
        _summarizationService = summarizationService;

        _audioRecorder.AudioLevelChanged += (s, level) =>
        {
            SendEvent("audio_level", new { level });
        };

        _transcriptionService.SegmentDiscovered += (s, segment) =>
        {
            SendEvent("segment", segment);
        };

        _transcriptionService.StatusChanged += (s, status) =>
        {
            SendEvent("transcription_status", new { status });
        };
    }

    public void Attach(WebView2 webView)
    {
        _webView = webView;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.TryGetWebMessageAsString();
            var json = JsonNode.Parse(raw);
            if (json == null) return;

            var id = json["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
            var command = json["command"]?.GetValue<string>() ?? string.Empty;
            var payload = json["payload"];

            try
            {
                var result = await HandleCommandAsync(command, payload);
                ReplySuccess(id, result);
            }
            catch (Exception ex)
            {
                ReplyError(id, ex.Message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebBridge] Error processing message: {ex.Message}");
        }
    }

    private async Task<object?> HandleCommandAsync(string command, JsonNode? payload)
    {
        switch (command)
        {
            case "api_get_meetings":
            {
                return await _db.GetAllMeetingsAsync();
            }

            case "api_get_meeting":
            {
                var id = payload?["id"]?.GetValue<string>() ?? string.Empty;
                return await _db.GetMeetingByIdAsync(id);
            }

            case "api_save_meeting_title":
            {
                var id = payload?["id"]?.GetValue<string>() ?? string.Empty;
                var title = payload?["title"]?.GetValue<string>() ?? "Untitled Meeting";
                await _db.UpdateMeetingTitleAsync(id, title);
                return true;
            }

            case "api_delete_meeting":
            {
                var id = payload?["id"]?.GetValue<string>() ?? string.Empty;
                await _db.DeleteMeetingAsync(id);
                return true;
            }

            case "api_search_transcripts":
            {
                var query = payload?["query"]?.GetValue<string>() ?? string.Empty;
                return await _db.SearchTranscriptsAsync(query);
            }

            case "api_start_recording":
            {
                _activeMeetingId = Guid.NewGuid().ToString();
                _recordingStartTime = DateTime.UtcNow;

                var settings = await _db.GetSettingsAsync();
                var recordingsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Meetily",
                    settings.AudioSaveDirectory
                );

                var meeting = new Meeting
                {
                    Id = _activeMeetingId,
                    Title = $"Meeting {DateTime.Now:yyyy-MM-dd HH:mm}",
                    CreatedAt = _recordingStartTime,
                    Status = "Recording"
                };

                var audioPath = await _audioRecorder.StartRecordingAsync(_activeMeetingId, recordingsFolder);
                meeting.AudioFilePath = audioPath;
                await _db.SaveMeetingAsync(meeting);

                return meeting;
            }

            case "api_stop_recording":
            {
                if (string.IsNullOrEmpty(_activeMeetingId))
                {
                    throw new InvalidOperationException("No active recording found.");
                }

                var meetingId = _activeMeetingId;
                _activeMeetingId = null;

                var audioPath = await _audioRecorder.StopRecordingAsync();
                var duration = (DateTime.UtcNow - _recordingStartTime).TotalSeconds;

                var meeting = await _db.GetMeetingByIdAsync(meetingId) ?? new Meeting { Id = meetingId };
                meeting.DurationSeconds = duration;
                meeting.Status = "Transcribing";
                await _db.SaveMeetingAsync(meeting);

                // Run Transcription & Summarization in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var settings = await _db.GetSettingsAsync();
                        SendEvent("status_update", new { meetingId, status = "Transcribing" });

                        // 1. Transcribe with Whisper
                        var segments = await _transcriptionService.TranscribeFileAsync(
                            audioPath,
                            settings.SelectedWhisperModel,
                            settings.LanguagePreference
                        );

                        meeting.Segments = segments;
                        meeting.Status = "Summarizing";
                        await _db.SaveMeetingAsync(meeting);
                        SendEvent("status_update", new { meetingId, status = "Summarizing" });

                        // 2. Summarize with Ollama if enabled
                        if (settings.AutoSummarize && segments.Count > 0)
                        {
                            var fullText = string.Join("\n", segments.ConvertAll(s => $"[{TimeSpan.FromSeconds(s.StartSeconds):mm\\:ss}] {s.Text}"));
                            var summaryResult = await _summarizationService.GenerateSummaryAsync(
                                settings.OllamaEndpoint,
                                settings.SelectedLlmModel,
                                fullText
                            );

                            meeting.Summary = summaryResult.Summary;
                            meeting.ActionItems = summaryResult.ActionItems;
                            meeting.KeyTopics = summaryResult.KeyTopics;
                        }

                        meeting.Status = "Completed";
                        await _db.SaveMeetingAsync(meeting);
                        SendEvent("meeting_completed", meeting);
                    }
                    catch (Exception ex)
                    {
                        meeting.Status = "Error";
                        await _db.SaveMeetingAsync(meeting);
                        SendEvent("meeting_error", new { meetingId, error = ex.Message });
                    }
                });

                return meeting;
            }

            case "api_get_model_config":
            {
                var settings = await _db.GetSettingsAsync();
                return new
                {
                    endpoint = settings.OllamaEndpoint,
                    selectedModel = settings.SelectedLlmModel,
                    autoSummarize = settings.AutoSummarize
                };
            }

            case "api_save_model_config":
            {
                var settings = await _db.GetSettingsAsync();
                if (payload?["endpoint"]?.GetValue<string>() is string ep) settings.OllamaEndpoint = ep;
                if (payload?["selectedModel"]?.GetValue<string>() is string m) settings.SelectedLlmModel = m;
                if (payload?["autoSummarize"]?.GetValue<bool>() is bool auto) settings.AutoSummarize = auto;
                await _db.SaveSettingsAsync(settings);
                return true;
            }

            case "api_get_transcript_config":
            {
                var settings = await _db.GetSettingsAsync();
                return new
                {
                    model = settings.SelectedWhisperModel,
                    language = settings.LanguagePreference
                };
            }

            case "api_save_transcript_config":
            {
                var settings = await _db.GetSettingsAsync();
                if (payload?["model"]?.GetValue<string>() is string m) settings.SelectedWhisperModel = m;
                if (payload?["language"]?.GetValue<string>() is string l) settings.LanguagePreference = l;
                await _db.SaveSettingsAsync(settings);
                return true;
            }

            case "get_ollama_models":
            {
                var settings = await _db.GetSettingsAsync();
                var endpoint = payload?["endpoint"]?.GetValue<string>() ?? settings.OllamaEndpoint;
                return await _summarizationService.ListAvailableModelsAsync(endpoint);
            }

            case "get_audio_devices":
            {
                return new
                {
                    inputs = _audioRecorder.GetInputDevices(),
                    outputs = _audioRecorder.GetOutputDevices()
                };
            }

            default:
                throw new NotSupportedException($"Command '{command}' is not supported.");
        }
    }

    private void ReplySuccess(string id, object? data)
    {
        _webView?.Dispatcher.Invoke(() =>
        {
            var msg = JsonSerializer.Serialize(new { id, status = "ok", data });
            _webView.CoreWebView2.PostWebMessageAsJson(msg);
        });
    }

    private void ReplyError(string id, string message)
    {
        _webView?.Dispatcher.Invoke(() =>
        {
            var msg = JsonSerializer.Serialize(new { id, status = "error", error = message });
            _webView.CoreWebView2.PostWebMessageAsJson(msg);
        });
    }

    private void SendEvent(string eventName, object? data)
    {
        _webView?.Dispatcher.Invoke(() =>
        {
            if (_webView?.CoreWebView2 != null)
            {
                var msg = JsonSerializer.Serialize(new { type = "event", @event = eventName, data });
                _webView.CoreWebView2.PostWebMessageAsJson(msg);
            }
        });
    }
}
