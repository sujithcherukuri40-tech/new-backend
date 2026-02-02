# Authentication Initialization Fix

## Problem
The application was getting stuck on the authentication loading screen ("Checking authentication...") and never showing the login page when opened.

## Root Causes Identified

### 1. **Missing Finally Block in AuthShellViewModel**
- The `InitializeAsync()` method set `IsInitializing = true` at the start
- If any exception occurred during initialization, `IsInitializing` was never set back to `false`
- This left the loading overlay visible permanently, blocking the login screen

### 2. **Silent Exception Swallowing in App.axaml.cs**
- The `ShowAuthShell` method had a try-catch in the `Opened` event handler that caught all exceptions silently
- When `InitializeAsync()` failed, users couldn't see what went wrong
- No fallback mechanism to ensure login screen was shown

### 3. **Improper CancellationTokenSource Usage**
- In `AuthSessionViewModel.InitializeAsync()`, a new CancellationTokenSource was created but not properly linked with the passed token
- This could cause resource leaks and timing issues

## Fixes Applied

### ? AuthShellViewModel.cs
```csharp
public async Task InitializeAsync()
{
    IsInitializing = true;
    InitializingMessage = "Checking authentication...";
    
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _authSession.InitializeAsync(cts.Token);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Auth session initialization failed: {ex.Message}");
    }
    finally
    {
        // ? CRITICAL: Always clear the initializing state
        IsInitializing = false;
    }
    
    // Navigate based on final state...
}
```

**Key Change**: Added `finally` block to guarantee `IsInitializing` is always set to `false`, regardless of success or failure.

### ? AuthSessionViewModel.cs
```csharp
public async Task InitializeAsync(CancellationToken cancellationToken = default)
{
    try
    {
        var hasTokens = await _tokenStorage.HasTokensAsync(cancellationToken);
        
        if (!hasTokens)
        {
            CurrentState = AuthState.CreateUnauthenticated();
            return;
        }

        // ? Properly link cancellation tokens
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        
        try
        {
            var result = await _authService.GetCurrentUserAsync(cts.Token);
            CurrentState = result.Success ? result.State : AuthState.CreateUnauthenticated();
            
            if (!result.Success)
                await _tokenStorage.ClearTokensAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate existing session, clearing tokens");
            await _tokenStorage.ClearTokensAsync(CancellationToken.None);
            CurrentState = AuthState.CreateUnauthenticated();
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Auth initialization failed");
        CurrentState = AuthState.CreateUnauthenticated();
    }
}
```

**Key Changes**:
- Used `CreateLinkedTokenSource` to properly link cancellation tokens
- Added proper logging for debugging
- Ensured state is always set to unauthenticated on failure

### ? App.axaml.cs
```csharp
authShell.Opened += async (_, _) =>
{
    try
    {
        await authShellViewModel.InitializeAsync();
    }
    catch (Exception ex)
    {
        // ? Log error instead of silently swallowing
        Console.WriteLine($"Auth initialization error: {ex.Message}");
        // The InitializeAsync method should handle this, but ensure we're not stuck
    }
};
```

**Key Change**: Added logging to help diagnose initialization failures in the future.

## Expected Behavior After Fix

### Normal Flow:
1. ? App starts ? Shows AuthShell with loading overlay
2. ? `InitializeAsync()` runs for max 5 seconds
3. ? Loading overlay disappears (guaranteed by finally block)
4. ? Either:
   - User is already authenticated ? Go to MainWindow
   - User has pending approval ? Show PendingApprovalView
   - User is not authenticated ? Show LoginView

### Error Flow:
1. ? App starts ? Shows AuthShell with loading overlay
2. ? `InitializeAsync()` fails or times out
3. ? Exception is caught and logged
4. ? **Finally block ensures loading overlay is hidden**
5. ? User sees LoginView (default fallback)

## Testing Recommendations

1. **Test with no internet connection**: Should show login screen within 5 seconds
2. **Test with valid stored tokens**: Should authenticate and open MainWindow
3. **Test with invalid stored tokens**: Should clear tokens and show login screen
4. **Test with pending approval user**: Should show PendingApprovalView
5. **Test API server down**: Should timeout gracefully and show login screen

## Build Status
? Build successful - All changes compile correctly
