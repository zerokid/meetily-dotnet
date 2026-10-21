using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Meetily.Core.Models;
using Meetily.Core.Services;

namespace Meetily.Data;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string? dbPath = null)
    {
        var finalPath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meetily.db");
        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = $"Data Source={finalPath};";
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS meetings (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                audio_file_path TEXT,
                summary TEXT,
                action_items_json TEXT,
                key_topics_json TEXT,
                status TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transcript_segments (
                id TEXT PRIMARY KEY,
                meeting_id TEXT NOT NULL,
                speaker TEXT NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NOT NULL,
                FOREIGN KEY (meeting_id) REFERENCES meetings (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_segments_meeting ON transcript_segments(meeting_id);
        ";
        await tableCmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Meeting>> GetAllMeetingsAsync()
    {
        var list = new List<Meeting>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, duration_seconds, audio_file_path, summary, action_items_json, key_topics_json, status FROM meetings ORDER BY created_at DESC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var meeting = new Meeting
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                DurationSeconds = reader.GetDouble(3),
                AudioFilePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                Summary = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = reader.GetString(8)
            };

            var actionsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            if (!string.IsNullOrEmpty(actionsJson))
            {
                meeting.ActionItems = JsonSerializer.Deserialize<List<string>>(actionsJson) ?? new();
            }

            var topicsJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            if (!string.IsNullOrEmpty(topicsJson))
            {
                meeting.KeyTopics = JsonSerializer.Deserialize<List<string>>(topicsJson) ?? new();
            }

            list.Add(meeting);
        }

        return list;
    }

    public async Task<Meeting?> GetMeetingByIdAsync(string meetingId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, duration_seconds, audio_file_path, summary, action_items_json, key_topics_json, status FROM meetings WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", meetingId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var meeting = new Meeting
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                DurationSeconds = reader.GetDouble(3),
                AudioFilePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                Summary = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = reader.GetString(8)
            };

            var actionsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            if (!string.IsNullOrEmpty(actionsJson))
            {
                meeting.ActionItems = JsonSerializer.Deserialize<List<string>>(actionsJson) ?? new();
            }

            var topicsJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            if (!string.IsNullOrEmpty(topicsJson))
            {
                meeting.KeyTopics = JsonSerializer.Deserialize<List<string>>(topicsJson) ?? new();
            }

            meeting.Segments = await GetSegmentsAsync(meetingId);
            return meeting;
        }

        return null;
    }

    public async Task SaveMeetingAsync(Meeting meeting)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO meetings (id, title, created_at, duration_seconds, audio_file_path, summary, action_items_json, key_topics_json, status)
            VALUES (@id, @title, @created_at, @duration_seconds, @audio_file_path, @summary, @action_items_json, @key_topics_json, @status)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                duration_seconds = excluded.duration_seconds,
                audio_file_path = excluded.audio_file_path,
                summary = excluded.summary,
                action_items_json = excluded.action_items_json,
                key_topics_json = excluded.key_topics_json,
                status = excluded.status;
        ";

        cmd.Parameters.AddWithValue("@id", meeting.Id);
        cmd.Parameters.AddWithValue("@title", meeting.Title);
        cmd.Parameters.AddWithValue("@created_at", meeting.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@duration_seconds", meeting.DurationSeconds);
        cmd.Parameters.AddWithValue("@audio_file_path", (object?)meeting.AudioFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@summary", (object?)meeting.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action_items_json", JsonSerializer.Serialize(meeting.ActionItems));
        cmd.Parameters.AddWithValue("@key_topics_json", JsonSerializer.Serialize(meeting.KeyTopics));
        cmd.Parameters.AddWithValue("@status", meeting.Status);

        await cmd.ExecuteNonQueryAsync();

        if (meeting.Segments.Count > 0)
        {
            await SaveSegmentsAsync(meeting.Id, meeting.Segments);
        }
    }

    public async Task DeleteMeetingAsync(string meetingId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM transcript_segments WHERE meeting_id = @id; DELETE FROM meetings WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", meetingId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateMeetingTitleAsync(string meetingId, string newTitle)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE meetings SET title = @title WHERE id = @id";
        cmd.Parameters.AddWithValue("@title", newTitle);
        cmd.Parameters.AddWithValue("@id", meetingId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<TranscriptSegment>> GetSegmentsAsync(string meetingId)
    {
        var list = new List<TranscriptSegment>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, meeting_id, speaker, start_seconds, end_seconds, text, confidence FROM transcript_segments WHERE meeting_id = @id ORDER BY start_seconds ASC";
        cmd.Parameters.AddWithValue("@id", meetingId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TranscriptSegment
            {
                Id = reader.GetString(0),
                MeetingId = reader.GetString(1),
                Speaker = reader.GetString(2),
                StartSeconds = reader.GetDouble(3),
                EndSeconds = reader.GetDouble(4),
                Text = reader.GetString(5),
                Confidence = reader.GetFloat(6)
            });
        }

        return list;
    }

    public async Task SaveSegmentsAsync(string meetingId, List<TranscriptSegment> segments)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var deleteCmd = connection.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM transcript_segments WHERE meeting_id = @id";
        deleteCmd.Parameters.AddWithValue("@id", meetingId);
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var seg in segments)
        {
            var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT INTO transcript_segments (id, meeting_id, speaker, start_seconds, end_seconds, text, confidence)
                VALUES (@id, @meeting_id, @speaker, @start_seconds, @end_seconds, @text, @confidence)
            ";
            insertCmd.Parameters.AddWithValue("@id", seg.Id);
            insertCmd.Parameters.AddWithValue("@meeting_id", meetingId);
            insertCmd.Parameters.AddWithValue("@speaker", seg.Speaker);
            insertCmd.Parameters.AddWithValue("@start_seconds", seg.StartSeconds);
            insertCmd.Parameters.AddWithValue("@end_seconds", seg.EndSeconds);
            insertCmd.Parameters.AddWithValue("@text", seg.Text);
            insertCmd.Parameters.AddWithValue("@confidence", seg.Confidence);
            await insertCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<List<Meeting>> SearchTranscriptsAsync(string query)
    {
        var list = new List<Meeting>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT m.id, m.title, m.created_at, m.duration_seconds, m.audio_file_path, m.summary, m.action_items_json, m.key_topics_json, m.status
            FROM meetings m
            JOIN transcript_segments s ON m.id = s.meeting_id
            WHERE s.text LIKE @query OR m.title LIKE @query OR m.summary LIKE @query
            ORDER BY m.created_at DESC
        ";
        cmd.Parameters.AddWithValue("@query", $"%{query}%");

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Meeting
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                DurationSeconds = reader.GetDouble(3),
                AudioFilePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                Summary = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = reader.GetString(8)
            });
        }

        return list;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = 'app_settings'";

        var val = await cmd.ExecuteScalarAsync() as string;
        if (!string.IsNullOrEmpty(val))
        {
            return JsonSerializer.Deserialize<AppSettings>(val) ?? new AppSettings();
        }

        return new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO settings (key, value) VALUES ('app_settings', @val)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";
        cmd.Parameters.AddWithValue("@val", JsonSerializer.Serialize(settings));
        await cmd.ExecuteNonQueryAsync();
    }
}
