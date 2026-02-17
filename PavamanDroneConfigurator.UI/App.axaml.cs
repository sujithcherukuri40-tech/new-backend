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
using PavamanDroneConfigurator.UI.Views.Admin;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DotNetEnv;

namespace PavamanDroneConfigurator.UI;

public partial class App : Application
{
    public static ServiceProvider? Services { get; private set; }
    public static IConfiguration? Configuration { get; private set; }
    private static bool _isShuttingDown;
    private static IConnectionService? _connectionService;

    // EMBEDDED API URL - No external config file needed
    private const string EMBEDDED_API_URL = "http://43.205.128.248:5000";

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
        // Build configuration with embedded defaults - no external file required
        var configData = new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = EMBEDDED_API_URL,
            ["Auth:ApiUrl"] = EMBEDDED_API_URL,
            ["Auth:AwsApiUrl"] = EMBEDDED_API_URL,
            ["Auth:UseAwsApi"] = "true",
            ["Auth:TokenExpiryBufferSeconds"] = "30",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft"] = "Warning"
        };

        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(configData);

        // Optionally load external config if exists (for development/override)
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            builder.AddJsonFile(configPath, optional: true, reloadOnChange: false);
        }

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
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton<ITokenStorage, SecureTokenStorage>();

        // Auto-connect settings storage
        services.AddSingleton<ConnectionSettingsStorage>();

        // Token authentication handler for API services requiring JWT
        services.AddTransient<TokenAuthenticationHandler>();

        // Get API URL - embedded default, can be overridden by env var
        var apiUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? EMBEDDED_API_URL;

        services.AddHttpClient<IAuthService, AuthApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<Core.Interfaces.IAdminService, AdminApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<TokenAuthenticationHandler>(); // ? Add JWT token handler

        services.AddHttpClient<FirmwareApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .AddHttpMessageHandler<TokenAuthenticationHandler>(); // ? Add JWT token handler

        services.AddSingleton<AuthSessionViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<PendingApprovalViewModel>();
        services.AddTransient<AuthShellViewModel>();
        services.AddTransient<ConnectionShellViewModel>();
        services.AddTransient<EntryPageViewModel>();

        services.AddTransient<UI.ViewModels.Admin.AdminPanelViewModel>();
        services.AddTransient<UI.ViewModels.Admin.AdminDashboardViewModel>();
        services.AddTransient<UI.ViewModels.Admin.FirmwareManagementViewModel>();

        // Local database is optional - only if connection string exists
        var connectionString = Configuration?.GetConnectionString("PostgresDb");
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddDbContext<DroneDbContext>(options =>
                options.UseNpgsql(connectionString)
            );
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
        services.AddTransient<ViewModels.Admin.ParamLogsViewModel>();

        Services = services.BuildServiceProvider();
        
        // Get connection service and monitor for disconnections
        _connectionService = Services.GetService<IConnectionService>();
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

    private void ShowEntryPage(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var entryPageViewModel = Services.GetRequiredService<EntryPageViewModel>();

            entryPageViewModel.FirmwareRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() => ShowFirmwareWindow(desktop));
            };

            entryPageViewModel.ConnectRequested += (_, _) =>
            {
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

            entryPageViewModel.AdminDashboardRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() => ShowAdminDashboardWindow(desktop));
            };

            entryPageViewModel.ExitRequested += (_, _) =>
            {
                _isShuttingDown = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            };

            var entryPage = new EntryPage { DataContext = entryPageViewModel };
            desktop.MainWindow = entryPage;
            entryPage.Show();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show entry page: {ex.Message}");
            ShowConnectionShell(desktop);
        }
    }

    private void ShowAdminDashboardWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var adminDashboardViewModel = Services.GetRequiredService<ViewModels.Admin.AdminDashboardViewModel>();
            var adminDashboardWindow = new AdminDashboardWindow { DataContext = adminDashboardViewModel };
            bool backRequested = false;

            adminDashboardWindow.BackRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                backRequested = true;
                var oldWindow = desktop.MainWindow;
                ShowEntryPage(desktop);
                oldWindow?.Close();
            };

            adminDashboardWindow.Closed += (_, _) =>
            {
                if (_isShuttingDown || backRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                    {
                        ShowEntryPage(desktop);
                    }
                });
            };

            var oldWindow = desktop.MainWindow;
            desktop.MainWindow = adminDashboardWindow;
            adminDashboardWindow.Show();
            oldWindow?.Close();

            // Initialize the dashboard data
            _ = Task.Run(async () =>
            {
                try
                {
                    await adminDashboardViewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Admin dashboard initialization error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show admin dashboard window: {ex.Message}");
        }
    }

    private void ShowFirmwareWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var firmwareViewModel = Services.GetRequiredService<FirmwarePageViewModel>();
            var firmwareWindow = new FirmwareWindow { DataContext = firmwareViewModel };
            bool backRequested = false;

            firmwareWindow.BackRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                backRequested = true;
                var oldWindow = desktop.MainWindow;
                ShowEntryPage(desktop);
                oldWindow?.Close();
            };

            firmwareWindow.Closed += (_, _) =>
            {
                if (_isShuttingDown || backRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                    {
                        ShowEntryPage(desktop);
                    }
                });
            };

            var oldWindow = desktop.MainWindow;
            desktop.MainWindow = firmwareWindow;
            firmwareWindow.Show();
            oldWindow?.Close();
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
            var connectionShellViewModel = Services.GetRequiredService<ConnectionShellViewModel>();

            connectionShellViewModel.ConnectionCompleted += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        ShowMainWindow(desktop);
                        oldWindow?.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during navigation: {ex.Message}");
                        ShowMainWindow(desktop);
                    }
                });
            };

            connectionShellViewModel.ConnectionCancelled += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
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

            // Subscribe to disconnection events to automatically return to ConnectionShell
            if (_connectionService != null)
            {
                _connectionService.ConnectionStateChanged += (sender, isConnected) =>
                {
                    if (!isConnected && !_isShuttingDown)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Console.WriteLine("Drone disconnected - returning to connection page");
                                var oldWindow = desktop.MainWindow;
                                ShowConnectionShell(desktop);
                                oldWindow?.Close();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error handling disconnection: {ex.Message}");
                            }
                        });
                    }
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