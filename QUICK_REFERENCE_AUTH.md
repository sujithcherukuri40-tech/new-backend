# Quick Reference: Authentication System

## How It Works

### 1. Application Startup Flow
```
App.axaml.cs
    ?
ShowAuthShell()
    ?
AuthShell.axaml ? AuthShellViewModel.InitializeAsync()
    ?
AuthSessionViewModel.InitializeAsync()
    ?
Decision: Has Tokens?
    ?? No ? Show Login Screen (instant)
    ?? Yes ? Validate with Server (1.5s timeout)
        ?? Success ? Open Main Window
        ?? Failure ? Clear Tokens ? Show Login
        ?? Timeout ? Clear Tokens ? Show Login
```

### 2. Login Flow
```
User enters credentials
    ?
LoginViewModel.LoginCommand
    ?
AuthSessionViewModel.LoginAsync()
    ?
AuthApiService.LoginAsync()
    ?
API Response
    ?? Success ? Store Tokens ? Open Main Window
    ?? Pending Approval ? Show Pending Screen
    ?? Error ? Show Error Message
```

### 3. Logout Flow
```
User clicks Logout
    ?
ProfilePageViewModel.LogoutCommand
    ?
AuthSessionViewModel.LogoutAsync()
    ?
Clear Local Tokens (instant)
    ?
Notify Server (fire & forget)
    ?
Show Auth Shell ? Login Screen
```

## Key Files

### Core Authentication
| File | Purpose |
|------|---------|
| `AuthSessionViewModel.cs` | Manages auth state, coordinates login/logout |
| `AuthShellViewModel.cs` | Controls auth UI flow (login/register/pending) |
| `LoginViewModel.cs` | Handles login form and validation |
| `RegisterViewModel.cs` | Handles registration form |
| `AuthApiService.cs` | HTTP client for auth API |
| `SecureTokenStorage.cs` | Encrypted token storage |

### UI Components
| File | Purpose |
|------|---------|
| `AuthShell.axaml` | Container for auth screens |
| `LoginView.axaml` | Login form UI |
| `RegisterView.axaml` | Registration form UI |
| `PendingApprovalView.axaml` | Pending approval screen |

### Configuration
| File | Purpose |
|------|---------|
| `App.axaml.cs` | DI container setup |
| `appsettings.json` | API URL configuration |
| `.env` | Environment variables |

## Common Tasks

### Add a New Auth Feature
```csharp
// 1. Add method to IAuthService
public interface IAuthService {
    Task<AuthResult> ResetPasswordAsync(string email);
}

// 2. Implement in AuthApiService
public async Task<AuthResult> ResetPasswordAsync(string email) {
    var response = await _httpClient.PostAsJsonAsync("/auth/reset-password", new { email });
    return await HandleAuthResponseAsync(response);
}

// 3. Add to AuthSessionViewModel
public async Task<AuthResult> ResetPasswordAsync(string email) {
    return await _authService.ResetPasswordAsync(email);
}

// 4. Create ViewModel
public class ForgotPasswordViewModel : ViewModelBase {
    [ObservableProperty]
    private string _email;

    [RelayCommand]
    private async Task ResetPasswordAsync() {
        var result = await _authSession.ResetPasswordAsync(Email);
    }
}
```

### Change Authentication Timeout
```csharp
// In AuthSessionViewModel.cs
cts.CancelAfter(TimeSpan.FromMilliseconds(1500)); // Current: 1.5 seconds

// To change:
cts.CancelAfter(TimeSpan.FromMilliseconds(3000)); // New: 3 seconds
```

### Change API URL
```json
// appsettings.json
{
  "Auth": {
    "ApiUrl": "https://your-api.com"
  }
}

// Or use environment variable
AUTH_API_URL=https://your-api.com
```

### Add Custom Error Handling
```csharp
// In LoginViewModel.cs
private static string GetUserFriendlyErrorMessage(AuthResult result)
{
    return result.ErrorCode switch
    {
        AuthErrorCode.InvalidCredentials => "Invalid email or password",
        AuthErrorCode.CustomError => "Your custom message", // Add here
        _ => result.ErrorMessage ?? "An error occurred"
    };
}
```

## Debugging

### Enable Verbose Logging
```csharp
// In App.axaml.cs
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug); // Change from Information
});
```

### Check Token Storage
```csharp
// Token file location:
%LocalAppData%\PavamanDroneConfigurator\Auth\tokens.dat

// To clear manually (for testing):
Delete the file and restart the app
```

### Common Issues & Solutions

#### Issue: App stuck on "Checking authentication..."
**Solution**: Already fixed! But if it happens:
1. Check if `finally { IsInitializing = false; }` exists in `AuthShellViewModel.InitializeAsync()`
2. Verify timeout is set correctly
3. Check logs for exceptions

#### Issue: Login button doesn't work
**Solution**:
1. Check if `CanLogin()` returns true
2. Verify email/password are not empty
3. Check `IsLoading` flag is false
4. Look for validation errors

#### Issue: Tokens not persisting
**Solution**:
1. Check if `SecureTokenStorage.StoreTokensAsync()` is called
2. Verify Windows DPAPI is available
3. Check file permissions on token storage path

#### Issue: API not reachable
**Solution**:
1. Verify `appsettings.json` has correct API URL
2. Check if API server is running
3. Verify network connectivity
4. Look for firewall/proxy issues

## Testing

### Manual Testing Checklist
```
? First-time user can register
? Registered user can login
? Invalid credentials show error
? Pending approval shows correct screen
? Logout works and returns to login
? App starts offline shows login
? App starts with valid tokens auto-logs in
? App starts with invalid tokens shows login
? Network timeout handled gracefully
```

### Unit Testing Examples
```csharp
[Fact]
public async Task LoginAsync_ValidCredentials_ReturnsSuccess()
{
    // Arrange
    var authService = new Mock<IAuthService>();
    authService.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>(), default))
        .ReturnsAsync(AuthResult.Succeeded(AuthState.CreateAuthenticated(new UserInfo())));
    
    var viewModel = new LoginViewModel(authService.Object);
    viewModel.Email = "test@test.com";
    viewModel.Password = "Password123";

    // Act
    await viewModel.LoginCommand.ExecuteAsync(null);

    // Assert
    Assert.False(viewModel.HasError);
}
```

## Performance Monitoring

### Key Metrics to Track
```csharp
// Startup time
_logger.LogInformation("Auth initialization started at {Time}", DateTime.UtcNow);
// ... later
_logger.LogInformation("Auth initialization completed in {Duration}ms", stopwatch.ElapsedMilliseconds);

// Login success rate
_logger.LogInformation("Login attempt for {Email}", email);
_logger.LogInformation("Login {Result} for {Email} in {Duration}ms", 
    result.Success ? "succeeded" : "failed", email, duration);

// Token validation
_logger.LogInformation("Token validation {Result} in {Duration}ms", 
    result.Success ? "succeeded" : "failed", duration);
```

## Security Best Practices

### ? Current Implementation
- Tokens encrypted with Windows DPAPI
- HTTPS required for API calls
- JWT with short expiration (15 minutes)
- Refresh token rotation on use
- Secure password hashing (BCrypt)

### ?? Additional Recommendations
- Enable 2FA for admin users
- Implement rate limiting on login attempts
- Add brute-force protection
- Log all authentication events
- Monitor for suspicious activity

## Quick Commands

### Clear All Tokens (for testing)
```powershell
Remove-Item "$env:LOCALAPPDATA\PavamanDroneConfigurator\Auth\tokens.dat" -Force
```

### View Logs
```powershell
# Console logs are shown in Debug Output window in Visual Studio
# Or redirect to file:
dotnet run 2>&1 | Tee-Object -FilePath "app.log"
```

### Build and Run
```powershell
dotnet build
dotnet run --project PavamanDroneConfigurator.UI
```

---

## Support

### If You Need Help
1. Check this guide first
2. Review the comprehensive documentation:
   - `PRODUCTION_AUTH_SYSTEM_COMPLETE_FIX.md`
   - `MVVM_ARCHITECTURE_COMPLIANCE_AUDIT.md`
   - `EXECUTIVE_SUMMARY_AUTH_FIX.md`
3. Check logs in Debug Output
4. Search codebase for similar patterns
5. Ask the team lead

### Contributing
When adding new features:
1. Follow MVVM pattern
2. Add logging at key points
3. Handle errors gracefully
4. Update documentation
5. Write unit tests

---

**Version**: 1.0.0  
**Last Updated**: 2025  
**Maintained By**: Development Team
