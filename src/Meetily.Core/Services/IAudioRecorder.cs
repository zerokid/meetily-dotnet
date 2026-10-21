using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Meetily.Core.Services;

public class AudioDevice
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    event EventHandler<float>? AudioLevelChanged;
    
    List<AudioDevice> GetInputDevices();
    List<AudioDevice> GetOutputDevices();
    
    Task<string> StartRecordingAsync(string meetingId, string outputFolder);
    Task<string> StopRecordingAsync();
}
