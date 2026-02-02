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
                        ShowMainWindow(desktop);
                        oldWindow?.Close();
                    }
                    catch
                    {
                        ShowMainWindow(desktop);
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