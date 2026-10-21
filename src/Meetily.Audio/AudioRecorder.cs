using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Meetily.Core.Services;

namespace Meetily.Audio;

public class AudioRecorder : IAudioRecorder
{
    private WasapiLoopbackCapture? _loopbackCapture;
    private WaveInEvent? _micCapture;
    private WaveFileWriter? _writer;
    private string? _currentFilePath;
    private bool _isRecording;

    private readonly object _lock = new();

    public bool IsRecording => _isRecording;
    public event EventHandler<float>? AudioLevelChanged;

    public List<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDevice
            {
                Id = i,
                Name = capabilities.ProductName,
                IsDefault = i == 0
            });
        }
        return devices;
    }

    public List<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        string defaultId = string.Empty;
        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = defaultDevice.ID;
        }
        catch
        {
            // Fallback if no default endpoint found
        }

        int index = 0;
        foreach (var endpoint in endpoints)
        {
            devices.Add(new AudioDevice
            {
                Id = index++,
                Name = endpoint.FriendlyName,
                IsDefault = !string.IsNullOrEmpty(defaultId) && endpoint.ID == defaultId
            });
        }
        return devices;
    }

    public Task<string> StartRecordingAsync(string meetingId, string outputFolder)
    {
        lock (_lock)
        {
            if (_isRecording)
            {
                return Task.FromResult(_currentFilePath ?? string.Empty);
            }

            Directory.CreateDirectory(outputFolder);
            _currentFilePath = Path.Combine(outputFolder, $"{meetingId}.wav");

            // Standard format for Whisper: 16 kHz, 16 bit, Mono
            var whisperFormat = new WaveFormat(16000, 16, 1);
            _writer = new WaveFileWriter(_currentFilePath, whisperFormat);

            // 1. Setup Windows Speaker Loopback Capture
            try
            {
                _loopbackCapture = new WasapiLoopbackCapture();
                _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
                _loopbackCapture.StartRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioRecorder] Warning: Loopback capture failed: {ex.Message}");
            }

            // 2. Setup Local Microphone Capture
            try
            {
                _micCapture = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1)
                };
                _micCapture.DataAvailable += OnMicDataAvailable;
                _micCapture.StartRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioRecorder] Warning: Microphone capture failed: {ex.Message}");
            }

            _isRecording = true;
            return Task.FromResult(_currentFilePath);
        }
    }

    private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (!_isRecording || _writer == null || _loopbackCapture == null) return;

            try
            {
                // Resample loopback to 16kHz 16-bit mono
                using var ms = new MemoryStream(e.Buffer, 0, e.BytesRecorded);
                using var rawStream = new RawSourceWaveStream(ms, _loopbackCapture.WaveFormat);
                var sampleProvider = rawStream.ToSampleProvider();
                var monoSample = sampleProvider.ToMono();
                var resampled = new WdlResamplingSampleProvider(monoSample, 16000);

                float[] buffer = new float[e.BytesRecorded];
                int samplesRead = resampled.Read(buffer, 0, buffer.Length);

                if (samplesRead > 0)
                {
                    _writer.WriteSamples(buffer, 0, samplesRead);
                    ComputePeakLevel(buffer, samplesRead);
                }
            }
            catch
            {
                // Ignore transient frame conversion glitches
            }
        }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (!_isRecording || _writer == null) return;

            // Direct 16kHz 16-bit mono write
            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            // Compute peak level
            float max = 0;
            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
                var sample32 = sample / 32768f;
                if (sample32 < 0) sample32 = -sample32;
                if (sample32 > max) max = sample32;
            }
            AudioLevelChanged?.Invoke(this, max);
        }
    }

    private void ComputePeakLevel(float[] buffer, int count)
    {
        float max = 0;
        for (int i = 0; i < count; i++)
        {
            float abs = Math.Abs(buffer[i]);
            if (abs > max) max = abs;
        }
        AudioLevelChanged?.Invoke(this, max);
    }

    public Task<string> StopRecordingAsync()
    {
        lock (_lock)
        {
            if (!_isRecording) return Task.FromResult(_currentFilePath ?? string.Empty);

            _isRecording = false;

            if (_loopbackCapture != null)
            {
                _loopbackCapture.StopRecording();
                _loopbackCapture.DataAvailable -= OnLoopbackDataAvailable;
                _loopbackCapture.Dispose();
                _loopbackCapture = null;
            }

            if (_micCapture != null)
            {
                _micCapture.StopRecording();
                _micCapture.DataAvailable -= OnMicDataAvailable;
                _micCapture.Dispose();
                _micCapture = null;
            }

            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }

            return Task.FromResult(_currentFilePath ?? string.Empty);
        }
    }

    public void Dispose()
    {
        StopRecordingAsync().GetAwaiter().GetResult();
    }
}
