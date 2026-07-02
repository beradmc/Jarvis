using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using JarvisCSharp.Services;
using JarvisCSharp.Audio;
using JarvisCSharp.AI;
using JarvisCSharp.Actions;
using JarvisCSharp.UI;

namespace JarvisCSharp;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<GeminiService>();
        services.AddSingleton<TtsService>();
        services.AddSingleton<WakeupListener>();
        services.AddSingleton<LiveAudioService>();
        services.AddSingleton<ActionManager>();
        services.AddSingleton<SystemShellService>();
        services.AddSingleton<HardwareMonitorService>();
        services.AddSingleton<UIAutomationService>();
        services.AddSingleton<LiveVisionService>();
        services.AddSingleton<WakeWordService>();
        services.AddSingleton<NativeOrbWindow>();

        // Automation Services
        services.AddSingleton<JarvisCSharp.Services.Automation.ActionLog>();
        services.AddSingleton<JarvisCSharp.Services.Automation.FlaUIService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.AutomationControllerService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.ContextManagerService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.SafetyGuardianService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.LearningSystemService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.WorkflowExecutorService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.ResearchAgentService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.VisionEngineService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.OCRService>();
        services.AddSingleton<JarvisCSharp.Services.Automation.ScreenCaptureModule>();
        services.AddSingleton<JarvisCSharp.Services.Automation.WhatsAppAutomationHelper>();

        // Actions
        services.AddSingleton<IAction, OpenAppAction>();
        services.AddSingleton<IAction, SaveMemoryAction>();
        services.AddSingleton<IAction, DeleteMemoryAction>();
        services.AddSingleton<IAction, MediaAction>();
        services.AddSingleton<IAction, BrowserAction>();
        services.AddSingleton<IAction, ShellAction>();
        services.AddSingleton<IAction, SysInfoAction>();
        services.AddSingleton<IAction, DesktopControlAction>();
        services.AddSingleton<IAction, ClipboardAction>();
        services.AddSingleton<IAction, WhatsappAction>();
        services.AddSingleton<IAction, WeatherAction>();
        services.AddSingleton<IAction, YoutubeStatsAction>();
        services.AddSingleton<IAction, CalendarAction>();
        services.AddSingleton<IAction, RemindersAction>();
        services.AddSingleton<IAction, HealthAction>();
        services.AddSingleton<IAction, ScreenVisionAction>();
        services.AddSingleton<IAction, UiControlAction>();
        services.AddSingleton<IAction, WindowControlAction>();
        services.AddSingleton<IAction, AdvancedWindowControlAction>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ActionManager'ı GeminiService'e bağla
        var gemini        = ServiceProvider?.GetRequiredService<GeminiService>();
        var actionManager = ServiceProvider?.GetRequiredService<ActionManager>();
        if (gemini != null && actionManager != null)
            gemini.SetActionManager(actionManager);

        var mainWindow = ServiceProvider?.GetRequiredService<MainWindow>();
        var orb = ServiceProvider?.GetRequiredService<NativeOrbWindow>();
        mainWindow?.Show();
    }
}
