using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for testing database connectivity.
/// This is a temporary page for Step 1 verification.
/// </summary>
public partial class DatabaseTestPageViewModel : ViewModelBase
{
    private readonly DatabaseTestService _dbTestService;
    private readonly ILogger<DatabaseTestPageViewModel> _logger;

    [ObservableProperty]
    private string _statusMessage = "Click 'Test Connection' to verify database connectivity";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isTesting = false;

    [ObservableProperty]
    private string _connectionInfo = "";

    public DatabaseTestPageViewModel(
        DatabaseTestService dbTestService,
        ILogger<DatabaseTestPageViewModel> logger)
    {
        _dbTestService = dbTestService;
        _logger = logger;
        
        // Get connection info on load
        ConnectionInfo = _dbTestService.GetConnectionInfo();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            IsTesting = true;
            StatusMessage = "Testing connection to PostgreSQL database...";
            IsConnected = false;

            var result = await _dbTestService.TestConnectionAsync();

            if (result)
            {
                IsConnected = true;
                StatusMessage = "? Database connection successful!\n" +
                               "You can now proceed with Step 2 (AWS Secrets Manager).";
            }
            else
            {
                IsConnected = false;
                StatusMessage = "? Database connection failed!\n" +
                               "Please check:\n" +
                               "1. Connection string in appsettings.json\n" +
                               "2. Database password\n" +
                               "3. Network connectivity to AWS RDS\n" +
                               "4. Security group settings";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"? Error testing connection:\n{ex.Message}";
            _logger.LogError(ex, "Error testing database connection");
        }
        finally
        {
            IsTesting = false;
        }
    }
}
