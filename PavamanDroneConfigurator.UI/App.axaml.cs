using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Repositories;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.UI.ViewModels;
using PavamanDroneConfigurator.UI.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI;

public partial class App : Application
{
    public static ServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // ArduPilot XML Integration Services (for parameter metadata)
        services.AddSingleton<ArduPilotXmlParser>();
        services.AddSingleton<ArduPilotMetadataDownloader>();
        services.AddSingleton<VehicleTypeDetector>();

        // ArduPilot JSON Metadata Loader (for Advanced Settings)
        services.AddSingleton<IArduPilotMetadataLoader, ArduPilotMetadataLoader>();

        // MAVLink Message Logger (for diagnostics and debugging)
        services.AddSingleton<IMavLinkMessageLogger, MavLinkMessageLogger>();

        // Calibration Services (Mission Planner-equivalent strict validation)
        services.AddSingleton<CalibrationPreConditionChecker>();
        services.AddSingleton<CalibrationAbortMonitor>();
        services.AddSingleton<CalibrationValidationHelper>();
        services.AddSingleton<AccelStatusTextParser>();
        services.AddSingleton<AccelImuValidator>();
        services.AddSingleton<AccelerometerCalibrationService>();

        // Firmware Services (Mission Planner-equivalent firmware flashing)
        services.AddSingleton<Stm32Bootloader>();
        services.AddSingleton<FirmwareDownloader>();
        services.AddSingleton<IFirmwareService, FirmwareService>();

        // Repositories (Data Layer)
        services.AddSingleton<IParameterMetadataRepository, ParameterMetadataRepository>();

        // Core services (Business Logic Layer)
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
        // Log Analyzer Services
        services.AddSingleton<ILogAnalyzerService, LogAnalyzerService>();
        services.AddSingleton<ILogEventDetector, LogEventDetector>();
        services.AddSingleton<ILogQueryEngine, LogQueryEngine>();
        services.AddSingleton<ILogExportService, LogExportService>();
        services.AddSingleton<IDerivedChannelProvider, DerivedChannelProvider>();

        // ViewModels (Presentation Layer)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ConnectionPageViewModel>();
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
        services.AddTransient<AdvancedSettingsPageViewModel>();
        services.AddTransient<FirmwarePageViewModel>();

        Services = services.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        string xmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ParameterMetadata.xml");
        System.Diagnostics.Debug.WriteLine("OUTPUT PATH => " + AppContext.BaseDirectory);
        System.Diagnostics.Debug.WriteLine("XML PATH => " + xmlPath);
        System.Diagnostics.Debug.WriteLine("Exists? " + File.Exists(xmlPath));
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var splashScreen = new SplashScreenWindow
            {
                DataContext = Services!.GetRequiredService<SplashScreenViewModel>()
            };

            splashScreen.Show();

            Task.Run(async () =>
            {
                try
                {
                    var splashViewModel = (SplashScreenViewModel)splashScreen.DataContext!;
                    await splashViewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    // Minimal fallback logging
                    Console.WriteLine($"Splash initialization failed: {ex.Message}");
                }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        desktop.MainWindow = new MainWindow
                        {
                            DataContext = Services!.GetRequiredService<MainWindowViewModel>(),
                        };
                        desktop.MainWindow.Show();
                        splashScreen.Close();
                    });
                }
            });

            desktop.Exit += (_, _) => Services?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
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