using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meetily.Core.Models;

namespace Meetily.Core.Services;

public interface ISummarizationService
{
    Task<bool> TestConnectionAsync(string endpoint);
    Task<List<string>> ListAvailableModelsAsync(string endpoint);
    Task<SummaryResult> GenerateSummaryAsync(string endpoint, string model, string fullTranscript, CancellationToken cancellationToken = default);
}
