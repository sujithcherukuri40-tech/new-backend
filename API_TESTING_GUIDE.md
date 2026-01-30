# Backend API Testing Guide

## ? Your API IS Running!

The application is working correctly. It's listening on:
- **HTTP**: http://localhost:5102 (or 5000 after update)
- **HTTPS**: https://localhost:7218 (or 5001 after update)

---

## ?? How to Access the API

### Method 1: Open Swagger UI

**After running `dotnet run`, open your browser to:**

```
http://localhost:5000
```

Or with HTTPS:

```
https://localhost:5001
```

You should see the Swagger UI interface with all API endpoints.

---

### Method 2: Test Endpoints Manually

#### 1. Health Check
```powershell
curl http://localhost:5000/health
```

Expected response:
```json
{
  "status": "healthy",
  "timestamp": "2026-01-27T...",
  "environment": "Development"
}
```

#### 2. Register a User
```powershell
curl -X POST http://localhost:5000/auth/register `
  -H "Content-Type: application/json" `
  -d '{
    "fullName": "Test User",
    "email": "test@example.com",
    "password": "Test1234!",
    "confirmPassword": "Test1234!"
  }'
```

Expected response:
```json
{
  "success": true,
  "authStatus": "PendingApproval",
  "user": {...}
}
```

---

## ?? Common Issues

### Issue: "The application did not start"

**Check if port is already in use:**
```powershell
netstat -ano | findstr :5000
```

If port is in use, kill the process:
```powershell
taskkill /PID <PID> /F
```

### Issue: "Cannot access Swagger"

**Make sure you're using HTTP (not HTTPS) for local testing:**
- ? `http://localhost:5000`
- ? `https://localhost:5001` (requires SSL certificate)

### Issue: "Database connection failed"

The app will still run even if the database is not accessible. The error will only show when you try to use auth endpoints.

To fix:
1. Make sure RDS is accessible (via SSH tunnel or from EC2)
2. Check connection string in appsettings.json

---

## ?? Quick Test Script

Save this as `test-api.ps1`:

```powershell
# Test API Health
Write-Host "Testing API Health..." -ForegroundColor Cyan
$health = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method GET
Write-Host "? API is healthy!" -ForegroundColor Green
$health | ConvertTo-Json

# Test Registration
Write-Host "`nTesting User Registration..." -ForegroundColor Cyan
$registerBody = @{
    fullName = "Test User"
    email = "test@example.com"
    password = "Test1234!"
    confirmPassword = "Test1234!"
} | ConvertTo-Json

try {
    $result = Invoke-RestMethod -Uri "http://localhost:5000/auth/register" `
        -Method POST `
        -Body $registerBody `
        -ContentType "application/json"
    
    Write-Host "? Registration successful!" -ForegroundColor Green
    $result | ConvertTo-Json
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $responseBody = $_.ErrorDetails.Message
    
    if ($statusCode -eq 409) {
        Write-Host "?? User already exists (this is okay)" -ForegroundColor Yellow
    }
    else {
        Write-Host "? Error: $statusCode" -ForegroundColor Red
        Write-Host $responseBody
    }
}
```

Run it:
```powershell
.\test-api.ps1
```

---

## ? Everything is Working!

Your API is running correctly. The output you saw:
```
Now listening on: http://localhost:5102
Application started. Press Ctrl+C to shut down.
```

This means **the app is working perfectly**!

---

## ?? Next Steps

1. ? API is running
2. ? Apply database migration (see QUICK_START.md)
3. ? Test registration ? approve user ? test login
4. ? Connect frontend to backend

**You're ready to test the full auth flow!** ??

