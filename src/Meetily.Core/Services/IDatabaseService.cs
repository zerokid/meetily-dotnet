using System.Collections.Generic;
using System.Threading.Tasks;
using Meetily.Core.Models;

namespace Meetily.Core.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    
    Task<List<Meeting>> GetAllMeetingsAsync();
    Task<Meeting?> GetMeetingByIdAsync(string meetingId);
    Task SaveMeetingAsync(Meeting meeting);
    Task DeleteMeetingAsync(string meetingId);
    Task UpdateMeetingTitleAsync(string meetingId, string newTitle);
    
    Task<List<TranscriptSegment>> GetSegmentsAsync(string meetingId);
    Task SaveSegmentsAsync(string meetingId, List<TranscriptSegment> segments);
    Task<List<Meeting>> SearchTranscriptsAsync(string query);
    
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}
