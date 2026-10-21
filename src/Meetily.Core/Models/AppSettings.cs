namespace Meetily.Core.Models;

public class AppSettings
{
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string SelectedLlmModel { get; set; } = "llama3:latest";
    public string SelectedWhisperModel { get; set; } = "base"; // tiny, base, small, medium
    public string LanguagePreference { get; set; } = "en";
    public bool AutoSummarize { get; set; } = true;
    public string AudioSaveDirectory { get; set; } = "recordings";
    public string ModelsDirectory { get; set; } = "models";
    public int AudioInputDeviceId { get; set; } = 0; // Microphone
    public int AudioLoopbackDeviceId { get; set; } = 0; // System speaker loopback
}
