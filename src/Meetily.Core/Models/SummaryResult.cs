using System.Collections.Generic;

namespace Meetily.Core.Models;

public class SummaryResult
{
    public string Summary { get; set; } = string.Empty;
    public List<string> ActionItems { get; set; } = new();
    public List<string> KeyTopics { get; set; } = new();
    public List<string> Decisions { get; set; } = new();
}
