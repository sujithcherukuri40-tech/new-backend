using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.UI.ViewModels.Auth;
using PavamanDroneConfigurator.UI.ViewModels.Admin;
using System.Collections.ObjectModel;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IPersistenceService _persistenceService;
    private readonly AuthSessionViewModel _authSession;
    private readonly ILogger<ProfilePageViewModel> _logger;
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminPanelViewModel> _adminPanelLogger;

    [ObservableProperty]
    private ObservableCollection<string> _profiles = new();

    [ObservableProperty]
    private string _selectedProfile = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // User Details Properties
    [ObservableProperty]
    private string _userFullName = string.Empty;

    [ObservableProperty]
    private string _userEmail = string.Empty;

    [ObservableProperty]
    private string _userRole = string.Empty;

    [ObservableProperty]
    private string _userStatus = string.Empty;

    [ObservableProperty]
    private string _accountCreatedDate = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isLoggingOut;

    /// <summary>
    /// Admin panel view model for user management.
    /// Only initialized when user is an admin.
    /// </summary>
    public AdminPanelViewModel? AdminPanel { get; private set; }

    /// <summary>
    /// Event raised when user requests logout and needs to navigate back to login
    /// </summary>
    public event EventHandler? LogoutRequested;

    public ProfilePageViewModel(
        IPersistenceService persistenceService,
        AuthSessionViewModel authSession,
        ILogger<ProfilePageViewModel> logger,
        IAdminService adminService,
        ILogger<AdminPanelViewModel> adminPanelLogger)
    {
        _persistenceService = persistenceService;
        _authSession = authSession;
        _logger = logger;
        _adminService = adminService;
        _adminPanelLogger = adminPanelLogger;

        // Load user details from auth session
        LoadUserDetails();

        // Initialize admin panel if user is admin
        if (IsAdmin)
        {
            AdminPanel = new AdminPanelViewModel(_adminService, _adminPanelLogger);
            _ = AdminPanel.InitializeAsync();
        }

        // Subscribe to auth state changes
        _authSession.StateChanged += OnAuthStateChanged;
        
        _logger.LogInformation("ProfilePageViewModel initialized");
    }

    private void OnAuthStateChanged(object? sender, Core.Models.Auth.AuthState state)
    {
        LoadUserDetails();
    }

    private void LoadUserDetails()
    {
        var user = _authSession.CurrentState.User;
        
        _logger.LogInformation("=== ProfilePage: Loading user details ===");
        _logger.LogInformation("User is null: {IsNull}", user == null);
        _logger.LogInformation("AuthSession CurrentState: {@State}", _authSession.CurrentState);
        
        if (user != null)
        {
            _logger.LogInformation("User found: {Email}, Role: {Role}, IsAdmin: {IsAdmin}", 
                user.Email, user.Role, user.IsAdmin);
            
            UserFullName = user.FullName;
            UserEmail = user.Email;
            UserRole = user.Role;
            var wasAdmin = IsAdmin;
            IsAdmin = user.IsAdmin;
            UserStatus = user.IsApproved ? "Approved" : "Pending Approval";
            AccountCreatedDate = $"Member since: {user.CreatedAt:MMMM dd, yyyy}";

            // Initialize admin panel if user became an admin and panel doesn't exist
            if (IsAdmin && !wasAdmin && AdminPanel == null)
            {
                _logger.LogInformation("Initializing AdminPanel for admin user");
                AdminPanel = new AdminPanelViewModel(_adminService, _adminPanelLogger);
                _ = AdminPanel.InitializeAsync();
                OnPropertyChanged(nameof(AdminPanel));
            }
            // Dispose admin panel if user is no longer an admin
            else if (!IsAdmin && wasAdmin && AdminPanel != null)
            {
                _logger.LogInformation("Disposing AdminPanel - user is no longer admin");
                AdminPanel = null;
                OnPropertyChanged(nameof(AdminPanel));
            }

            _logger.LogInformation("Successfully loaded user profile: {Email} ({Role})", user.Email, user.Role);
        }
        else
        {
            _logger.LogWarning("No user found in auth session - using fallback values");
            UserFullName = "Guest User";
            UserEmail = "Not logged in";
            UserRole = "User";
            UserStatus = "Not authenticated";
            AccountCreatedDate = DateTime.Now.ToString("MMMM dd, yyyy 'at' hh:mm tt");
            IsAdmin = false;
        }
        
        _logger.LogInformation("=== ProfilePage: User details loaded successfully ===");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsLoggingOut) return;

        IsLoggingOut = true;
        StatusMessage = "Logging out...";

        try
        {
            _logger.LogInformation("User requested logout from profile page");
            
            await _authSession.LogoutAsync();
            
            StatusMessage = "Logged out successfully";
            _logger.LogInformation("User logged out successfully");

            // Raise event to trigger navigation to login page
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            StatusMessage = $"Logout failed: {ex.Message}";
        }
        finally
        {
            IsLoggingOut = false;
        }
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        var profiles = await _persistenceService.GetProfileNamesAsync();
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
        StatusMessage = $"Found {Profiles.Count} profiles";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            StatusMessage = "Please enter a profile name";
            return;
        }

        var data = new Dictionary<string, object>
        {
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["createdBy"] = UserEmail
        };

        var success = await _persistenceService.SaveProfileAsync(NewProfileName, data);
        StatusMessage = success ? $"Profile '{NewProfileName}' saved" : "Failed to save profile";

        if (success)
        {
            await LoadProfilesAsync();
            NewProfileName = string.Empty;
        }
    }

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
        {
            StatusMessage = "Please select a profile";
            return;
        }

        var data = await _persistenceService.LoadProfileAsync(SelectedProfile);
        StatusMessage = data != null ? $"Profile '{SelectedProfile}' loaded" : "Failed to load profile";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authSession.StateChanged -= OnAuthStateChanged;
        }
        base.Dispose(disposing);
    }
}
