using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.UI.ViewModels.Auth;
using System.Collections.ObjectModel;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class ProfilePageViewModel : ViewModelBase
{
    private readonly IPersistenceService _persistenceService;
    private readonly AuthSessionViewModel _authSession;
    private readonly ILogger<ProfilePageViewModel> _logger;

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
    private string _lastLoginDate = string.Empty;

    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isLoggingOut;

    /// <summary>
    /// Whether the user can logout (inverse of IsLoggingOut for binding)
    /// </summary>
    public bool CanLogout => !IsLoggingOut;

    /// <summary>
    /// Whether the user is a regular user (not admin)
    /// </summary>
    public bool IsUser => !IsAdmin;

    /// <summary>
    /// Event raised when user requests logout and needs to navigate back to login
    /// </summary>
    public event EventHandler? LogoutRequested;

    public ProfilePageViewModel(
        IPersistenceService persistenceService,
        AuthSessionViewModel authSession,
        ILogger<ProfilePageViewModel> logger)
    {
        _persistenceService = persistenceService;
        _authSession = authSession;
        _logger = logger;

        // Load user details from auth session
        LoadUserDetails();

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
        
        _logger.LogInformation("Loading user details. User is null: {IsNull}", user == null);
        
        if (user != null)
        {
            UserId = user.Id;
            UserFullName = user.FullName;
            UserEmail = user.Email;
            UserRole = user.Role;
            IsAdmin = user.IsAdmin;
            UserStatus = user.IsApproved ? "Approved" : "Pending Approval";
            AccountCreatedDate = user.CreatedAt.ToString("MMMM dd, yyyy 'at' hh:mm tt");
            LastLoginDate = user.LastLoginAt?.ToString("MMMM dd, yyyy 'at' hh:mm tt") ?? "Never";

            _logger.LogDebug("Loaded user profile: {Email} ({Role})", user.Email, user.Role);
        }
        else
        {
            UserId = string.Empty;
            UserFullName = "Guest User";
            UserEmail = "Not logged in";
            UserRole = "User";
            UserStatus = "Not authenticated";
            AccountCreatedDate = DateTime.Now.ToString("MMMM dd, yyyy 'at' hh:mm tt");
            LastLoginDate = "Never";
            IsAdmin = false;
            
            _logger.LogWarning("No user found in auth session");
        }
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

    partial void OnIsLoggingOutChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLogout));
    }

    partial void OnIsAdminChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUser));
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
