# ============================================================
#  KFT Drone Configurator - API Endpoint Health Check
#  Target: http://13.233.82.9:5000
# ============================================================

$BASE = "http://13.233.82.9:5000"
$results = [System.Collections.Generic.List[PSCustomObject]]::new()
$adminToken = $null
$userToken  = $null

function Test-Endpoint {
    param(
        [string]$Label,
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers = @{},
        [object]$Body = $null,
        [int[]]$ExpectedCodes = @(200)
    )

    try {
        $params = @{
            Uri             = $Url
            Method          = $Method
            Headers         = $Headers
            ContentType     = "application/json"
            TimeoutSec      = 15
            UseBasicParsing = $true
            ErrorAction     = "Stop"
        }
        if ($Body) { $params.Body = ($Body | ConvertTo-Json -Compress) }

        $resp = Invoke-WebRequest @params
        $status = $resp.StatusCode
        $ok = $status -in $ExpectedCodes
        $script:results.Add([PSCustomObject]@{
            Endpoint = $Label
            Status   = $status
            Result   = if ($ok) { "PASS" } else { "FAIL (unexpected $status)" }
        })
        return $resp
    }
    catch [System.Net.WebException] {
        $code = [int]$_.Exception.Response.StatusCode
        $ok   = $code -in $ExpectedCodes
        $script:results.Add([PSCustomObject]@{
            Endpoint = $Label
            Status   = $code
            Result   = if ($ok) { "PASS" } else { "FAIL ($code)" }
        })
        return $null
    }
    catch {
        $script:results.Add([PSCustomObject]@{
            Endpoint = $Label
            Status   = "N/A"
            Result   = "ERROR: $($_.Exception.Message)"
        })
        return $null
    }
}

# -------------------------------------------------------
# 1. Health check (no auth)
# -------------------------------------------------------
Test-Endpoint -Label "GET  /health" -Method GET `
    -Url "$BASE/health" -ExpectedCodes @(200) | Out-Null

# -------------------------------------------------------
# 2. Register a test user (may return 409 if already exists)
# -------------------------------------------------------
$testEmail    = "healthcheck_$(Get-Random -Max 99999)@kft-test.local"
$testPassword = "Health@Check9!"
Test-Endpoint -Label "POST /auth/register" -Method POST `
    -Url "$BASE/auth/register" `
    -Body @{ email = $testEmail; password = $testPassword; fullName = "Health Check Bot" } `
    -ExpectedCodes @(200, 201) | Out-Null

# -------------------------------------------------------
# 3. Login
# -------------------------------------------------------
$loginResp = Test-Endpoint -Label "POST /auth/login" -Method POST `
    -Url "$BASE/auth/login" `
    -Body @{ email = $testEmail; password = $testPassword } `
    -ExpectedCodes @(200, 201)

if ($loginResp) {
    try {
        $loginData  = $loginResp.Content | ConvertFrom-Json
        $userToken  = $loginData.tokens.accessToken
        $refreshTok = $loginData.tokens.refreshToken
    } catch { }
}

# -------------------------------------------------------
# 4. GET /auth/me  (requires bearer token)
# -------------------------------------------------------
if ($userToken) {
    Test-Endpoint -Label "GET  /auth/me" -Method GET `
        -Url "$BASE/auth/me" `
        -Headers @{ Authorization = "Bearer $userToken" } `
        -ExpectedCodes @(200) | Out-Null
} else {
    $results.Add([PSCustomObject]@{ Endpoint = "GET  /auth/me"; Status = "SKIP"; Result = "No token (login failed)" })
}

# -------------------------------------------------------
# 5. POST /auth/refresh
# -------------------------------------------------------
if ($refreshTok -and $userToken) {
    Test-Endpoint -Label "POST /auth/refresh" -Method POST `
        -Url "$BASE/auth/refresh" `
        -Body @{ accessToken = $userToken; refreshToken = $refreshTok } `
        -ExpectedCodes @(200, 201) | Out-Null
} else {
    $results.Add([PSCustomObject]@{ Endpoint = "POST /auth/refresh"; Status = "SKIP"; Result = "No token (login failed)" })
}

# -------------------------------------------------------
# 6. POST /auth/forgot-password (always returns 200 for security)
# -------------------------------------------------------
Test-Endpoint -Label "POST /auth/forgot-password" -Method POST `
    -Url "$BASE/auth/forgot-password" `
    -Body @{ email = $testEmail } `
    -ExpectedCodes @(200) | Out-Null

# -------------------------------------------------------
# 7. POST /auth/reset-password (invalid code — expect 400)
# -------------------------------------------------------
Test-Endpoint -Label "POST /auth/reset-password (invalid code→400)" -Method POST `
    -Url "$BASE/auth/reset-password" `
    -Body @{ email = $testEmail; code = "000000"; newPassword = "ShouldFail@1" } `
    -ExpectedCodes @(400) | Out-Null

# -------------------------------------------------------
# 8. POST /auth/logout
# -------------------------------------------------------
if ($userToken -and $refreshTok) {
    Test-Endpoint -Label "POST /auth/logout" -Method POST `
        -Url "$BASE/auth/logout" `
        -Headers @{ Authorization = "Bearer $userToken" } `
        -Body @{ refreshToken = $refreshTok } `
        -ExpectedCodes @(200, 204) | Out-Null
} else {
    $results.Add([PSCustomObject]@{ Endpoint = "POST /auth/logout"; Status = "SKIP"; Result = "No token (login failed)" })
}

# -------------------------------------------------------
# 9. Admin endpoints — login as admin first
#    (update credentials below if needed)
# -------------------------------------------------------
$adminEmail    = "admin@kftdrones.com"
$adminPassword = "Admin@KFT2024!"

$adminLoginResp = Test-Endpoint -Label "POST /auth/login (admin)" -Method POST `
    -Url "$BASE/auth/login" `
    -Body @{ email = $adminEmail; password = $adminPassword } `
    -ExpectedCodes @(200, 201)

if ($adminLoginResp) {
    try {
        $adminToken = ($adminLoginResp.Content | ConvertFrom-Json).tokens.accessToken
    } catch { }
}

if ($adminToken) {
    Test-Endpoint -Label "GET  /admin/users" -Method GET `
        -Url "$BASE/admin/users" `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -ExpectedCodes @(200) | Out-Null

    Test-Endpoint -Label "GET  /admin/parameter-locks" -Method GET `
        -Url "$BASE/admin/parameter-locks" `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -ExpectedCodes @(200) | Out-Null
} else {
    foreach ($ep in @("GET  /admin/users", "GET  /admin/parameter-locks")) {
        $results.Add([PSCustomObject]@{ Endpoint = $ep; Status = "SKIP"; Result = "Admin login failed" })
    }
}

# -------------------------------------------------------
# Results table
# -------------------------------------------------------
Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  KFT API Health Check — $BASE" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
$results | ForEach-Object {
    $color = switch -Wildcard ($_.Result) {
        "PASS"  { "Green"  }
        "SKIP*" { "Yellow" }
        default { "Red"    }
    }
    Write-Host ("[{0,-6}] {1,-50} {2}" -f $_.Status, $_.Endpoint, $_.Result) -ForegroundColor $color
}
Write-Host ""
$pass = ($results | Where-Object { $_.Result -eq "PASS" }).Count
$fail = ($results | Where-Object { $_.Result -match "^FAIL|^ERROR" }).Count
$skip = ($results | Where-Object { $_.Result -match "^SKIP" }).Count
Write-Host "  PASS: $pass  |  FAIL: $fail  |  SKIP: $skip" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
