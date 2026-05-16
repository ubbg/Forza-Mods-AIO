using System.Windows;
using Forza_Mods_AIO.Cheats;
using Forza_Mods_AIO.Helpers;
using Forza_Mods_AIO.Models;
using Forza_Mods_AIO.Resources;
using Forza_Mods_AIO.Resources.Keybinds;
using Forza_Mods_AIO.Resources.Theme;
using Forza_Mods_AIO.Services;
using Forza_Mods_AIO.ViewModels.Windows;
using Forza_Mods_AIO.Views.Windows;
using MahApps.Metro.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Forza_Mods_AIO.Resources.Cheats;

namespace Forza_Mods_AIO;

public partial class App
{
    private const string MutexName = "{(4A771E61-6684-449F-8952-B31582A8877E)}";
    private Mutex _mutex = null!;

    private static readonly IHost Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            c.SetBasePath(AppContext.BaseDirectory);
        }).
        ConfigureServices((_, services) =>
        {
            services.AddHostedService<ApplicationHostService>();
            
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MetroWindow, MainWindow>();

            services.AddSingleton<DebugWindowViewModel>();
            services.AddSingleton<DebugWindow>();
        }).Build();
    
    public static T GetRequiredService<T>() where T : class
    {
        return Host.Services.GetRequiredService<T>();
    }
    
    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        await Host.StartAsync();
        HotkeysManager.SetupSystemHook();
    }

    private async void App_OnExit(object sender, ExitEventArgs e)
    {
        HotkeysManager.ShutdownSystemHook();
        DisconnectFromGame();
        
        await Host.StopAsync();
        Host.Dispose();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);

        if (createdNew)
        {
            base.OnStartup(e);
            Privileges.TryEnableSeDebugPrivilege();
            SetupExceptionHandling();
            
            // Initialize the theme
            Theming.GetInstance().InitializeTheme();
            
            // Load saved language
            var savedLanguage = Settings.LoadLanguage();
            if (!string.IsNullOrEmpty(savedLanguage))
            {
                try
                {
                    var uri = new Uri($"/Resources/Translations/{savedLanguage}.xaml", UriKind.Relative);
                    var langDict = new ResourceDictionary { Source = uri };

                    // Remove existing language dictionary if it exists
                    var resources = Current.Resources.MergedDictionaries;
                    var langDictToRemove = resources.FirstOrDefault(dict => 
                        dict.Source?.OriginalString.Contains("/Resources/Translations/") == true);
                        
                    if (langDictToRemove != null)
                    {
                        resources.Remove(langDictToRemove);
                    }

                    // Add the new language dictionary
                    resources.Add(langDict);
                }
                catch
                {
                    // If loading fails, it will fallback to English
                }
            }
        }
        else
        {
            MessageBox.Show("Another instance of the tool is already running.", "Information", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            Current.Shutdown();
        }
    }
    
    // https://stackoverflow.com/a/46804709
    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");

        DispatcherUnhandledException += (_, e) =>
        {
            ReportException(e.Exception, "Application.Current.DispatcherUnhandledException");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }
    
    private static void ReportException(Exception exception, string source)
    {
        MessageBox.Show(
            $"An unexpected error happened.\nPlease (Press Ctrl+C) to copy, and make an issue on the github repository or post the copied text on the aio mega-thread in our discord server (discord.gg/forzamods)\n\nSource:{source}\nException:{exception.Message}\nException Source:{exception.Source}\n\nTool Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}\nGame: {GameVerPlat.GetInstance().Name}\nGame Version: {GameVerPlat.GetInstance().Update}\nPlatform: {GameVerPlat.GetInstance().Platform}",
            $"{GetRequiredService<MetroWindow>().Title} - Error",
            0,
            MessageBoxImage.Error
        );
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        base.OnExit(e);
    }

    private static void DisconnectFromGame()
    {
        foreach (var cheatInstance in CachedInstances.Where(kv => typeof(ICheatsBase).IsAssignableFrom(kv.Key)))
        {
            ((ICheatsBase)cheatInstance.Value).Cleanup();
        }
        _ = Imports.CloseHandle(Forza_Mods_AIO.Resources.Memory.GetInstance().MProc.Handle);
    }
}