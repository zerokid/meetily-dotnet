using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meetily.Core.Models;

namespace Meetily.Core.Services;

public interface ITranscriptionService
{
    event EventHandler<string>? StatusChanged;
    event EventHandler<TranscriptSegment>? SegmentDiscovered;
    
    Task<bool> IsModelDownloadedAsync(string modelName);
    Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken cancellationToken = default);
    Task<List<TranscriptSegment>> TranscribeFileAsync(string audioFilePath, string modelName, string language = "auto", CancellationToken cancellationToken = default);
}
