using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Meetily.AI;
using Meetily.Audio;
using Meetily.Core.Services;
using Meetily.Data;

namespace Meetily.App;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // Initialize Database
        var db = ServiceProvider.GetRequiredService<IDatabaseService>();
        await db.InitializeAsync();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meetily");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "meetily.db");
        var modelsPath = Path.Combine(appData, "models");

        services.AddSingleton<IDatabaseService>(new DatabaseService(dbPath));
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<ITranscriptionService>(new WhisperTranscriptionService(modelsPath));
        services.AddSingleton<ISummarizationService, OllamaSummarizationService>();
        services.AddSingleton<WebBridge>();
        services.AddSingleton<MainWindow>();
    }
}
