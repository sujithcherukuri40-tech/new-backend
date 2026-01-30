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
using System.Threading.Tasks;
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
            Console.WriteLine("? Loaded environment variables from .env file");
        }
        else
        {
            Console.WriteLine("??  No .env file found, using system environment variables");
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
            builder.SetMinimumLevel(LogLevel.Information);
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
            
            var timeoutSeconds = int.TryParse(
                Environment.GetEnvironmentVariable("API_TIMEOUT_SECONDS"), 
                out var timeout) ? timeout : 30;
            
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            
            Console.WriteLine($"?? Auth API URL: {authApiUrl}");
            Console.WriteLine($"?? Using AWS API: {useAwsApi}");
        });
        
        // Register admin service with same HTTP client configuration
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
        
        services.AddSingleton<AuthSessionViewModel>();
        
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<PendingApprovalViewModel>();
        services.AddTransient<AuthShellViewModel>();
        
        services.AddTransient<UI.ViewModels.Admin.AdminPanelViewModel>();
        services.AddTransient<UI.ViewModels.Admin.AdminDashboardViewModel>();

        if (Configuration != null)
        {
            var connectionString = Configuration.GetConnectionString("PostgresDb");
            if (!string.IsNullOrEmpty(connectionString))
            {
                services.AddDbContext<DroneDbContext>(options =>
                    options.UseNpgsql(connectionString)
                        .EnableSensitiveDataLogging()
                        .LogTo(Console.WriteLine, LogLevel.Information)
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
        services.AddSingleton<AccelerometerCalibrationService>();

        services.AddSingleton<Stm32Bootloader>();
        services.AddSingleton<FirmwareDownloader>();
        services.AddSingleton<IFirmwareService, FirmwareService>();

        services.AddSingleton<IParameterMetadataRepository, ParameterMetadataRepository>();

        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IParameterService, ParameterService>();
        // Temporary stub implementation to prevent application crashes
        // TODO: Implement full CalibrationService when ready
        services.AddSingleton<ICalibrationService, CalibrationServiceStub>();
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
                        if (desktop.MainWindow != null)
                        {
                            var oldWindow = desktop.MainWindow;
                            ShowMainWindow(desktop);
                            oldWindow.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error transitioning to main window: {ex.Message}");
                        ShowMainWindow(desktop);
                    }
                });
            };

            authShellViewModel.AuthenticationCancelled += (_, _) =>
            {
                _isShuttingDown = true;
                Dispatcher.UIThread.Post(() => desktop.Shutdown());
            };

            var authShell = new AuthShell
            {
                DataContext = authShellViewModel
            };

            desktop.MainWindow = authShell;
            
            authShell.Opened += async (_, _) =>
            {
                try
                {
                    await authShell.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auth shell initialization error: {ex.Message}");
                }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auth initialization failed: {ex.Message}");
            ShowMainWindow(desktop);
        }
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_isShuttingDown || Services == null) return;

        try
        {
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
            
            // Subscribe to logout event from profile page
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
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during logout: {ex.Message}");
                        }
                    });
                };
            }
            
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to show main window: {ex.Message}");
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