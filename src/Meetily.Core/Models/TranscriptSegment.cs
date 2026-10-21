using System;

namespace Meetily.Core.Models;

public class TranscriptSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MeetingId { get; set; } = string.Empty;
    public string Speaker { get; set; } = "Speaker";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; } = 1.0f;
}
