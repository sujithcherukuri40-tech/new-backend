using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Repositories;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.Infrastructure.Services.Auth;
using PavamanDroneConfigurator.Infrastructure.Data;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.ViewModels.Auth;
using PavamanDroneConfigurator.UI.Views;
using PavamanDroneConfigurator.UI.Views.Auth;
using System;
using System.IO;
using System.Linq;
using DotNetEnv;

namespace PavamanDroneConfigurator.UI;

public partial class App : Application
{
    public static ServiceProvider? Services { get; private set; }
    public static IConfiguration? Configuration { get; private set; }
    private static bool _isShuttingDown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        BuildConfiguration();
        ConfigureServices();
    }

    private void BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        Configuration = builder.Build();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        if (Configuration != null)
        {
            services.AddSingleton<IConfiguration>(Configuration);
        }

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information); // Show important auth logs
        });

        services.AddSingleton<ITokenStorage, SecureTokenStorage>();

        services.AddHttpClient<IAuthService, AuthApiService>(client =>
        {
            var useAwsApi = Configuration?.GetValue<bool>("Auth:UseAwsApi") ?? false;
            var authApiUrl = useAwsApi
                ? (Environment.GetEnvironmentVariable("AWS_API_URL")
                   ?? Configuration?.GetValue<string>("Auth:AwsApiUrl")
                   ?? "http://localhost:5000")
                : (Environment.GetEnvironmentVariable("AUTH_API_URL")
                   ?? Configuration?.GetValue<string>("Auth:ApiUrl")
                   ?? "http://localhost:5000");

            client.BaseAddress = new Uri(authApiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(10); // Reasonable timeout for auth calls
        });

        services.AddHttpClient<Core.Interfaces.IAdminService, AdminApiService>(client =>
        {
            var useAwsApi = Configuration?.GetValue<bool>("Auth:UseAwsApi") ?? false;
            var authApiUrl = useAwsApi
                ? (Environment.GetEnvironmentVariable("AWS_API_URL")
                   ?? Configuration?.GetValue<string>("Auth:AwsApiUrl")
                   ?? "http://localhost:5000")
                : (Environment.GetEnvironmentVariable("AUTH_API_URL")
                   ?? Configuration?.GetValue<string>("Auth:ApiUrl")
                   ?? "http://localhost:5000");

            client.BaseAddress = new Uri(authApiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Firmware API HTTP Client for S3 integration
        // PRODUCTION: Desktop app should NOT access AWS directly - use API only
        services.AddHttpClient<FirmwareApiService>(client =>
        {
            var apiUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
                ?? Configuration?.GetValue<string>("Api:BaseUrl")
                ?? "http://localhost:5000";
            
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for firmware downloads
        });
        
        // ? REMOVED: Direct AWS S3 access from desktop app (security risk)
        // Desktop app should only call backend API, not AWS directly
        // services.AddSingleton<AwsS3Service>(); // REMOVED FOR PRODUCTION

        services.AddSingleton<AuthSessionViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<PendingApprovalViewModel>();
        services.AddTransient<AuthShellViewModel>();
        services.AddTransient<ConnectionShellViewModel>();
        services.AddTransient<EntryPageViewModel>(); // NEW: Entry Page ViewModel

        services.AddTransient<UI.ViewModels.Admin.AdminPanelViewModel>();
        services.AddTransient<UI.ViewModels.Admin.AdminDashboardViewModel>();
        services.AddTransient<UI.ViewModels.Admin.FirmwareManagementViewModel>();

        if (Configuration != null)
        {
            var connectionString = Configuration.GetConnectionString("PostgresDb");
            if (!string.IsNullOrEmpty(connectionString))
            {
                services.AddDbContext<DroneDbContext>(options =>
                    options.UseNpgsql(connectionString)
                );
            }
        }

        services.AddSingleton<DatabaseTestService>();
        services.AddSingleton<ArduPilotXmlParser>();
        services.AddSingleton<ArduPilotMetadataDownloader>();
        services.AddSingleton<VehicleTypeDetector>();
        services.AddSingleton<IArduPilotMetadataLoader, ArduPilotMetadataLoader>();
        services.AddSingleton<IMavLinkMessageLogger, MavLinkMessageLogger>();
        services.AddSingleton<CalibrationPreConditionChecker>();
        services.AddSingleton<CalibrationAbortMonitor>();
        services.AddSingleton<CalibrationValidationHelper>();
        // Note: AccelerometerCalibrationService functionality is now integrated into CalibrationService
        services.AddSingleton<Stm32Bootloader>();
        services.AddSingleton<FirmwareDownloader>();
        services.AddSingleton<IFirmwareService, FirmwareService>();
        services.AddSingleton<IParameterMetadataRepository, ParameterMetadataRepository>();
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IParameterService, ParameterService>();
        services.AddSingleton<ICalibrationService, CalibrationService>();
        services.AddSingleton<ISafetyService, SafetyService>();
        services.AddSingleton<IAirframeService, AirframeService>();
        services.AddSingleton<IPersistenceService, PersistenceService>();
        services.AddSingleton<IFlightModeService, FlightModeService>();
        services.AddSingleton<IMotorEscService, MotorEscService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<IPidTuningService, PidTuningService>();
        services.AddSingleton<ISerialConfigService, SerialConfigService>();
        services.AddSingleton<IRcCalibrationService, RcCalibrationService>();
        services.AddSingleton<ISensorConfigService, SensorConfigService>();
        services.AddSingleton<IParameterMetadataService, ParameterMetadataService>();
        services.AddSingleton<IDroneInfoService, DroneInfoService>();
        services.AddSingleton<ILogAnalyzerService, LogAnalyzerService>();
        services.AddSingleton<ILogEventDetector, LogEventDetector>();
        services.AddSingleton<ILogQueryEngine, LogQueryEngine>();
        services.AddSingleton<ILogExportService, LogExportService>();
        services.AddSingleton<IDerivedChannelProvider, DerivedChannelProvider>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ConnectionPageViewModel>();
        services.AddTransient<DatabaseTestPageViewModel>();
        services.AddTransient<AirframePageViewModel>();
        services.AddTransient<ParametersPageViewModel>();
        services.AddTransient<SafetyPageViewModel>();
        services.AddTransient<ProfilePageViewModel>();
        services.AddTransient<SplashScreenViewModel>();
        services.AddTransient<FlightModePageViewModel>();
        services.AddTransient<PowerPageViewModel>();
        services.AddTransient<MotorEscPageViewModel>();
        services.AddTransient<PidTuningPageViewModel>();
        services.AddTransient<SerialConfigPageViewModel>();
        services.AddTransient<RcCalibrationPageViewModel>();
        services.AddTransient<SensorsCalibrationPageViewModel>();
        services.AddTransient<DroneDetailsPageViewModel>();
        services.AddTransient<ParameterMetadataViewModel>();
        services.AddTransient<LogAnalyzerPageViewModel>();
        services.AddTransient<ResetParametersPageViewModel>();
        services.AddTransient<SprayingConfigPageViewModel>();
        services.AddTransient<CameraConfigPageViewModel>();
        services.AddTransient<AdvancedSettingsPageViewModel>();
        services.AddTransient<FirmwarePageViewModel>();

        Services = services.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            ShowAuthShell(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowAuthShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var authShellViewModel = Services.GetRequiredService<AuthShellViewModel>();

            authShellViewModel.AuthenticationCompleted += (_, _) =>
            {
                if (_isShuttingDown) return;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        // CHANGED: Navigate to Entry Page instead of Connection Shell
                        ShowEntryPage(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowEntryPage(desktop);
                    }
                });
            };

            authShellViewModel.AuthenticationCancelled += (_, _) =>
            {
                _isShuttingDown = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            };

            var authShell = new AuthShell { DataContext = authShellViewModel };
            desktop.MainWindow = authShell;
            authShell.Show();

            // IMMEDIATELY initialize - don't wait for Opened event
            _ = Task.Run(async () =>
            {
                try
                {
                    await authShellViewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auth initialization error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show auth shell: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows the Entry Page - the gateway after authentication.
    /// User can choose to Flash Firmware or Connect to Drone.
    /// </summary>
    private void ShowEntryPage(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            Console.WriteLine("[App] ShowEntryPage called");
            var entryPageViewModel = Services.GetRequiredService<EntryPageViewModel>();

            // Handle Firmware button - opens standalone firmware window
            entryPageViewModel.FirmwareRequested += (_, _) =>
            {
                Console.WriteLine("[App] FirmwareRequested event received");
                if (_isShuttingDown) return;

                Dispatcher.UIThread.Post(() =>
                {
                    ShowFirmwareWindow(desktop);
                });
            };

            // Handle Connect button - navigates to Connection Shell
            entryPageViewModel.ConnectRequested += (_, _) =>
            {
                Console.WriteLine("[App] ConnectRequested event received");
                if (_isShuttingDown) return;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        ShowConnectionShell(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowConnectionShell(desktop);
                    }
                });
            };

            // Handle Exit button - shuts down the application
            entryPageViewModel.ExitRequested += (_, _) =>
            {
                Console.WriteLine("[App] ExitRequested event received");
                _isShuttingDown = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            };

            var entryPage = new EntryPage { DataContext = entryPageViewModel };
            desktop.MainWindow = entryPage;
            entryPage.Show();
            Console.WriteLine("[App] EntryPage shown");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show entry page: {ex.Message}");
            // Fallback to connection shell if entry page fails
            ShowConnectionShell(desktop);
        }
    }

    /// <summary>
    /// Shows the standalone Firmware Window.
    /// Replaces the main window - closing firmware window returns to Entry Page.
    /// </summary>
    private void ShowFirmwareWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            Console.WriteLine("[App] ShowFirmwareWindow called");
            var firmwareViewModel = Services.GetRequiredService<FirmwarePageViewModel>();
            
            var firmwareWindow = new FirmwareWindow { DataContext = firmwareViewModel };
            
            // Track if back was requested to avoid double navigation
            bool backRequested = false;
            
            // When back button is clicked, return to Entry Page
            firmwareWindow.BackRequested += (_, _) =>
            {
                Console.WriteLine("[App] FirmwareWindow BackRequested event received");
                if (_isShuttingDown) return;
                
                backRequested = true;
                
                // Show entry page first, then close firmware window
                var oldWindow = desktop.MainWindow;
                ShowEntryPage(desktop);
                oldWindow?.Close();
                
                Console.WriteLine("[App] Navigated back to Entry Page from Firmware Window");
            };
            
            // Handle window closing via X button (only if back wasn't already requested)
            firmwareWindow.Closed += (_, _) =>
            {
                Console.WriteLine("[App] FirmwareWindow Closed event received");
                if (_isShuttingDown || backRequested) return;

                Dispatcher.UIThread.Post(() =>
                {
                    // Only show entry page if no main window exists (X button was clicked)
                    if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                    {
                        ShowEntryPage(desktop);
                    }
                });
            };
            
            // Close the entry page and show firmware window as main window
            var oldWindow = desktop.MainWindow;
            desktop.MainWindow = firmwareWindow;
            firmwareWindow.Show();
            oldWindow?.Close();
            
            Console.WriteLine("[App] FirmwareWindow shown, Entry Page closed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show firmware window: {ex.Message}");
        }
    }

    private void ShowConnectionShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            Console.WriteLine("[App] ShowConnectionShell called");
            var connectionShellViewModel = Services.GetRequiredService<ConnectionShellViewModel>();

            connectionShellViewModel.ConnectionCompleted += (_, _) =>
            {
                Console.WriteLine("[App] ConnectionCompleted event received!");
                if (_isShuttingDown) return;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Console.WriteLine("[App] Navigating to MainWindow...");
                        var oldWindow = desktop.MainWindow;
                        ShowMainWindow(desktop);
                        oldWindow?.Close();
                        Console.WriteLine("[App] MainWindow shown, old window closed");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[App] Error during navigation: {ex.Message}");
                        ShowMainWindow(desktop);
                    }
                });
            };

            connectionShellViewModel.ConnectionCancelled += (_, _) =>
            {
                Console.WriteLine("[App] ConnectionCancelled event received");
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        // CHANGED: Return to Entry Page instead of Auth Shell
                        ShowEntryPage(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowEntryPage(desktop);
                    }
                });
            };

            var connectionShell = new ConnectionShell { DataContext = connectionShellViewModel };
            desktop.MainWindow = connectionShell;
            connectionShell.Show();
            Console.WriteLine("[App] ConnectionShell shown");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show connection shell: {ex.Message}");
        }
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();

            if (mainViewModel.ProfilePage != null)
            {
                mainViewModel.ProfilePage.LogoutRequested += (_, _) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var oldWindow = desktop.MainWindow;
                            ShowAuthShell(desktop);
                            oldWindow?.Close();
                        }
                        catch { }
                    });
                };
            }

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch
        {
            _isShuttingDown = true;
            desktop.Shutdown();
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}