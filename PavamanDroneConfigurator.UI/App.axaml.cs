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
    private static IParameterService? _parameterService;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    
    // Track if we're already handling a disconnection to prevent duplicate navigation
    private static bool _isHandlingDisconnection;
    // Track the current connection state handler to avoid multiple subscriptions
    private static EventHandler<bool>? _currentConnectionHandler;

    // EMBEDDED API URL - No external config file needed
    private const string EMBEDDED_API_URL = "http://13.235.13.233:5000";

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
        
        // Subscribe to session expiration event to redirect to login
        TokenAuthenticationHandler.SessionExpired += OnSessionExpired;
    }

    /// <summary>
    /// Handles session expiration by redirecting user to login screen.
    /// Called when a 401 Unauthorized response is received from the API.
    /// </summary>
    private static void OnSessionExpired(object? sender, EventArgs e)
    {
        if (_isShuttingDown || _desktop == null) return;
        
        Console.WriteLine("Session expired - redirecting to login screen");
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_isShuttingDown || _desktop == null || Services == null) return;
                
                // Get the auth session and clear the state
                var authSession = Services.GetService<AuthSessionViewModel>();
                if (authSession != null)
                {
                    // Force logout to clear state
                    _ = authSession.LogoutAsync();
                }
                
                // Close current window and show auth shell for re-login
                var oldWindow = _desktop.MainWindow;
                ShowAuthShellStatic(_desktop);
                oldWindow?.Close();
                
                Console.WriteLine("Navigated to login screen due to session expiration");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling session expiration: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Static method to show auth shell for re-login after session expiration.
    /// </summary>
    private static void ShowAuthShellStatic(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            // Unsubscribe from previous connection handler if any
            UnsubscribeConnectionHandler();
            
            var authShellViewModel = Services.GetRequiredService<AuthShellViewModel>();

            authShellViewModel.AuthenticationCompleted += (_, _) =>
            {
                if (_isShuttingDown) return;

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        ShowEntryPageStatic(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowEntryPageStatic(desktop);
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
    
    /// <summary>
    /// Static method to show entry page after re-authentication.
    /// </summary>
    private static void ShowEntryPageStatic(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            // Unsubscribe from previous connection handler if any
            UnsubscribeConnectionHandler();
            
            var entryPageViewModel = Services.GetRequiredService<EntryPageViewModel>();

            entryPageViewModel.FirmwareRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() => ShowFirmwareWindowStatic(desktop));
            };

            entryPageViewModel.ConnectRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var oldWindow = desktop.MainWindow;
                        ShowConnectionShellStatic(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowConnectionShellStatic(desktop);
                    }
                });
            };

            entryPageViewModel.AdminDashboardRequested += (_, _) =>
            {
                if (_isShuttingDown) return;
                Dispatcher.UIThread.Post(() => ShowAdminDashboardWindowStatic(desktop));
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
            ShowConnectionShellStatic(desktop);
        }
    }
    
    private static void ShowAdminDashboardWindowStatic(IClassicDesktopStyleApplicationLifetime desktop)
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
                ShowEntryPageStatic(desktop);
                oldWindow?.Close();
            };

            adminDashboardWindow.Closed += (_, _) =>
            {
                if (_isShuttingDown || backRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                    {
                        ShowEntryPageStatic(desktop);
                    }
                });
            };

            var oldWindow = desktop.MainWindow;
            desktop.MainWindow = adminDashboardWindow;
            adminDashboardWindow.Show();
            oldWindow?.Close();

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

    private static void ShowFirmwareWindowStatic(IClassicDesktopStyleApplicationLifetime desktop)
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
                ShowEntryPageStatic(desktop);
                oldWindow?.Close();
            };

            firmwareWindow.Closed += (_, _) =>
            {
                if (_isShuttingDown || backRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
                    {
                        ShowEntryPageStatic(desktop);
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

    private static void ShowConnectionShellStatic(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            // Unsubscribe from previous connection handler
            UnsubscribeConnectionHandler();
            
            // Reset disconnection handling flag
            _isHandlingDisconnection = false;
            
            // Clear parameter state to ensure fresh download on reconnect
            if (_parameterService != null)
            {
                Console.WriteLine("[App] Clearing parameter state for fresh reconnection");
                _parameterService.ClearParameters();
            }
            
            var connectionShellViewModel = Services.GetRequiredService<ConnectionShellViewModel>();

            connectionShellViewModel.ConnectionCompleted += async (_, _) =>
            {
                if (_isShuttingDown) return;
                
                // IMPORTANT: Wait a moment to ensure parameters are fully loaded
                // This prevents flickering when showing MainWindow
                await Task.Delay(100);
                
                // Double-check parameters are actually complete before showing MainWindow
                if (_parameterService != null && !_parameterService.IsParameterDownloadComplete)
                {
                    Console.WriteLine("[App] Parameters not fully complete, waiting...");
                    // Wait a bit longer and check again
                    await Task.Delay(500);
                }
                
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // Final check before navigation
                        if (_parameterService != null && !_parameterService.IsParameterDownloadComplete)
                        {
                            Console.WriteLine("[App] Parameters still not ready, aborting navigation");
                            return;
                        }
                        
                        Console.WriteLine("[App] Parameters fully loaded, showing MainWindow");
                        var oldWindow = desktop.MainWindow;
                        ShowMainWindowStatic(desktop);
                        oldWindow?.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during navigation: {ex.Message}");
                        ShowMainWindowStatic(desktop);
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
                        ShowEntryPageStatic(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowEntryPageStatic(desktop);
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
    
    /// <summary>
    /// Unsubscribe from the connection state changed event to prevent duplicate handlers
    /// </summary>
    private static void UnsubscribeConnectionHandler()
    {
        if (_connectionService != null && _currentConnectionHandler != null)
        {
            _connectionService.ConnectionStateChanged -= _currentConnectionHandler;
            _currentConnectionHandler = null;
            Console.WriteLine("[App] Unsubscribed from connection state handler");
        }
    }

    private static void ShowMainWindowStatic(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            // Unsubscribe from any previous connection handler first
            UnsubscribeConnectionHandler();
            
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
                            ShowAuthShellStatic(desktop);
                            oldWindow?.Close();
                        }
                        catch { }
                    });
                };
            }

            // CRITICAL: Ensure telemetry is running when MainWindow opens
            // If connection exists, force telemetry service to start and request streams
            if (_connectionService != null && _connectionService.IsConnected)
            {
                var telemetryService = Services.GetService<ITelemetryService>();
                if (telemetryService != null)
                {
                    Console.WriteLine("[App] ✓ Connection active - ensuring telemetry is running...");
                    telemetryService.RequestStreams(); // Force immediate stream request
                }
            }

            // Create a single connection handler and store reference for later cleanup
            if (_connectionService != null)
            {
                _currentConnectionHandler = (sender, isConnected) =>
                {
                    // Only handle disconnection once, and only if not already handling
                    if (!isConnected && !_isShuttingDown && !_isHandlingDisconnection)
                    {
                        _isHandlingDisconnection = true;
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Console.WriteLine("[App] Drone disconnected - returning to connection page");
                                // First show the new window, THEN close the old one
                                // This prevents app from closing due to no windows being open
                                var oldWindow = desktop.MainWindow;
                                
                                // Create and show connection shell FIRST
                                ShowConnectionShellStatic(desktop);
                                
                                // Now close the old window
                                oldWindow?.Close();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error handling disconnection: {ex.Message}");
                                _isHandlingDisconnection = false;
                            }
                        });
                    }
                };
                
                _connectionService.ConnectionStateChanged += _currentConnectionHandler;
                Console.WriteLine("[App] Subscribed to connection state handler");
            }

            // Subscribe to disconnect request from MainWindow navbar button
            mainViewModel.DisconnectRequested += (_, _) =>
            {
                if (_isShuttingDown || _isHandlingDisconnection) return;
                _isHandlingDisconnection = true;
                
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Console.WriteLine("[App] Disconnect requested - returning to connection page");
                        // First show the new window, THEN close the old one
                        var oldWindow = desktop.MainWindow;
                        
                        // Create and show connection shell FIRST
                        ShowConnectionShellStatic(desktop);
                        
                        // Now close the old window
                        oldWindow?.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling disconnect: {ex.Message}");
                        _isHandlingDisconnection = false;
                    }
                });
            };

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing main window: {ex.Message}");
            // Don't shutdown on error, try to recover
            try
            {
                ShowConnectionShellStatic(desktop);
            }
            catch
            {
                _isShuttingDown = true;
                desktop.Shutdown();
            }
        }
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
        Console.WriteLine("[App] ========== ConfigureServices START ==========");
        
        var services = new ServiceCollection();

        if (Configuration != null)
        {
            services.AddSingleton<IConfiguration>(Configuration);
        }

        Console.WriteLine("[App] Adding logging services...");
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information); // Changed from Warning to Information for better diagnostics
        });

        Console.WriteLine("[App] Adding core services...");
        services.AddSingleton<ITokenStorage, SecureTokenStorage>();

        // Auto-connect settings storage
        services.AddSingleton<ConnectionSettingsStorage>();

        // Token authentication handler for API services requiring JWT
        services.AddTransient<TokenAuthenticationHandler>();

        // Get API URL - embedded default, can be overridden by env var
        var apiUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? EMBEDDED_API_URL;

        Console.WriteLine($"[App] API URL: {apiUrl}");

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
        .AddHttpMessageHandler<TokenAuthenticationHandler>();

        // Also register AdminApiService as a concrete type so ParameterLockManagementViewModel
        // (which injects AdminApiService directly rather than IAdminService) can be resolved by DI.
        services.AddHttpClient<AdminApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<TokenAuthenticationHandler>();

        services.AddHttpClient<FirmwareApiService>(client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .AddHttpMessageHandler<TokenAuthenticationHandler>();

        services.AddSingleton<AuthSessionViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<ForgotPasswordViewModel>();
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

        var missionDbConnection = Configuration?.GetConnectionString("MissionDraftsDb");
        services.AddDbContext<MissionDbContext>(options =>
            options.UseSqlite(string.IsNullOrWhiteSpace(missionDbConnection)
                ? MissionDbContext.DefaultConnectionString
                : missionDbConnection));

        Console.WriteLine("[App] Adding infrastructure services...");
        services.AddSingleton<DatabaseTestService>();
        services.AddSingleton<ArduPilotXmlParser>();
        services.AddSingleton<ArduPilotMetadataDownloader>();
        services.AddSingleton<VehicleTypeDetector>();
        services.AddSingleton<IArduPilotMetadataLoader, ArduPilotMetadataLoader>();
        services.AddSingleton<IMavLinkMessageLogger, MavLinkMessageLogger>();
        services.AddSingleton<IVideoStreamingService, VideoStreamingService>();
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
        
        Console.WriteLine("[App] Adding TelemetryService...");
        services.AddSingleton<ITelemetryService, TelemetryService>();

        Console.WriteLine("[App] Adding Parameter Lock services...");
        services.AddSingleton<ParameterLockValidator>();
        services.AddSingleton<ParamLockApiService>(sp =>
        {
            var httpClient = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(EMBEDDED_API_URL)
            };
            var logger = sp.GetRequiredService<ILogger<ParamLockApiService>>();
            return new ParamLockApiService(httpClient, logger);
        });

        Console.WriteLine("[App] Adding ViewModels...");
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
        services.AddTransient<LiveMapPageViewModel>();
        services.AddTransient<TelemetryPageViewModel>();
        services.AddSingleton<PavamanDroneConfigurator.UI.Services.ParameterChangeLogService>();
        services.AddSingleton<MavlinkLogsViewModel>();
        services.AddSingleton<LiveCameraViewModel>();
        services.AddTransient<ViewModels.Admin.ParamLogsViewModel>(sp =>
            new ViewModels.Admin.ParamLogsViewModel(
                sp.GetRequiredService<PavamanDroneConfigurator.Infrastructure.Services.FirmwareApiService>(),
                sp.GetService<PavamanDroneConfigurator.Infrastructure.Services.Auth.AdminApiService>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<ViewModels.Admin.ParamLogsViewModel>>()));
        services.AddTransient<ViewModels.Admin.ParameterLockManagementViewModel>();

        Console.WriteLine("[App] Building service provider...");
        Services = services.BuildServiceProvider();
        Console.WriteLine("[App] Service provider built successfully!");
        
        try
        {
            // CRITICAL: Eagerly initialize TelemetryService so it subscribes to ConnectionService events
            // This ensures telemetry starts working immediately when connection is established
            Console.WriteLine("[App] ========== INITIALIZING TELEMETRY SERVICE ==========");
            var telemetryService = Services.GetRequiredService<ITelemetryService>();
            Console.WriteLine($"[App] SUCCESS: TelemetryService created successfully");
            Console.WriteLine($"[App] - State: {telemetryService.CurrentState}");
            Console.WriteLine($"[App] - IsReceiving: {telemetryService.IsReceivingTelemetry}");
            Console.WriteLine($"[App] - HasValidPosition: {telemetryService.HasValidPosition}");
            
            // Get connection service and parameter service for state management
            _connectionService = Services.GetService<IConnectionService>();
            _parameterService = Services.GetService<IParameterService>();

            Console.WriteLine($"[App] SUCCESS: ConnectionService: {(_connectionService != null ? "OK" : "NULL")}");
            Console.WriteLine($"[App] SUCCESS: ParameterService: {(_parameterService != null ? "OK" : "NULL")}");
            
            if (_connectionService != null)
            {
                Console.WriteLine($"[App] ConnectionService.IsConnected: {_connectionService.IsConnected}");
            }
            
            Console.WriteLine("[App] ========== SERVICE INITIALIZATION COMPLETE ==========");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] CRITICAL ERROR initializing services: {ex.Message}");
            Console.WriteLine($"[App] Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[App] Inner exception: {ex.InnerException.Message}");
                Console.WriteLine($"[App] Inner stack trace: {ex.InnerException.StackTrace}");
            }
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            DisableAvaloniaDataAnnotationValidation();
            ShowSplashScreen(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void ShowSplashScreen(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var splashViewModel = Services.GetRequiredService<SplashScreenViewModel>();
            var splashWindow = new SplashScreenWindow { DataContext = splashViewModel };
            
            desktop.MainWindow = splashWindow;
            splashWindow.Show();

            await splashViewModel.InitializeAsync();

            if (!_isShuttingDown)
            {
                ShowAuthShell(desktop);
                splashWindow.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show splash screen: {ex.Message}");
            ShowAuthShell(desktop);
        }
    }

    private void ShowAuthShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowAuthShellStatic(desktop);
    }

    private void ShowEntryPage(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowEntryPageStatic(desktop);
    }

    private void ShowAdminDashboardWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowAdminDashboardWindowStatic(desktop);
    }

    private void ShowFirmwareWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowFirmwareWindowStatic(desktop);
    }

    private void ShowConnectionShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowConnectionShellStatic(desktop);
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ShowMainWindowStatic(desktop);
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
