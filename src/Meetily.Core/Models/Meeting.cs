using System;
using System.Collections.Generic;

namespace Meetily.Core.Models;

public class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled Meeting";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public double DurationSeconds { get; set; }
    public string? AudioFilePath { get; set; }
    public string? Summary { get; set; }
    public List<string> ActionItems { get; set; } = new();
    public List<string> KeyTopics { get; set; } = new();
    public List<TranscriptSegment> Segments { get; set; } = new();
    public string Status { get; set; } = "Completed"; // Recording, Transcribing, Summarizing, Completed, Error
}
