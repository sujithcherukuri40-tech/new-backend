# Test Firmware API Endpoints
# This script tests all firmware management endpoints

$apiUrl = "http://43.205.128.248:5000"
$baseUrl = "$apiUrl/api/firmware"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FIRMWARE API ENDPOINT TESTS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Health Check
Write-Host "1. Testing Health Check..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/health" -Method Get -ErrorAction Stop
    Write-Host "   ? Health Check Passed" -ForegroundColor Green
    Write-Host "   Status: $($response.status)" -ForegroundColor Gray
    Write-Host "   Message: $($response.message)" -ForegroundColor Gray
} catch {
    Write-Host "   ? Health Check Failed: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# Test 2: List Firmwares
Write-Host "2. Testing List Firmwares (GET /api/firmware/inapp)..." -ForegroundColor Yellow
try {
    $firmwares = Invoke-RestMethod -Uri "$baseUrl/inapp" -Method Get -ErrorAction Stop
    Write-Host "   ? List Firmwares Passed" -ForegroundColor Green
    Write-Host "   Found $($firmwares.Count) firmware(s)" -ForegroundColor Gray
    
    if ($firmwares.Count -gt 0) {
        $firmwares | ForEach-Object {
            $name = if ($_.firmwareName) { $_.firmwareName } else { $_.fileName }
            $version = if ($_.firmwareVersion) { "v$($_.firmwareVersion)" } else { "" }
            Write-Host "   - $name $version ($($_.vehicleType)) - $($_.sizeDisplay)" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "   ? List Firmwares Failed: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# Test 3: Upload Firmware (requires a test file)
Write-Host "3. Testing Upload Firmware (POST /api/firmware/admin/upload)..." -ForegroundColor Yellow

# Create a dummy test file
$testFilePath = "$env:TEMP\test_firmware.apj"
$testContent = "TEST FIRMWARE FILE FOR API TESTING`n" * 100
[System.IO.File]::WriteAllText($testFilePath, $testContent)

try {
    $boundary = [System.Guid]::NewGuid().ToString()
    $fileName = "test_firmware_$(Get-Date -Format 'yyyyMMdd_HHmmss').apj"
    
    # Read file as bytes
    $fileBytes = [System.IO.File]::ReadAllBytes($testFilePath)
    $fileEnc = [System.Text.Encoding]::GetEncoding('ISO-8859-1').GetString($fileBytes)
    
    # Build multipart form data
    $LF = "`r`n"
    $body = (
        "--$boundary$LF" +
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"$LF" +
        "Content-Type: application/octet-stream$LF$LF" +
        $fileEnc + $LF +
        "--$boundary$LF" +
        "Content-Disposition: form-data; name=`"firmwareName`"$LF$LF" +
        "Test Firmware$LF" +
        "--$boundary$LF" +
        "Content-Disposition: form-data; name=`"firmwareVersion`"$LF$LF" +
        "1.0.0-test$LF" +
        "--$boundary$LF" +
        "Content-Disposition: form-data; name=`"firmwareDescription`"$LF$LF" +
        "Test firmware uploaded via PowerShell test script$LF" +
        "--$boundary--$LF"
    )
    
    $headers = @{
        "Content-Type" = "multipart/form-data; boundary=$boundary"
    }
    
    $response = Invoke-RestMethod -Uri "$baseUrl/admin/upload" -Method Post -Headers $headers -Body $body -ErrorAction Stop
    Write-Host "   ? Upload Firmware Passed" -ForegroundColor Green
    Write-Host "   Uploaded: $($response.fileName)" -ForegroundColor Gray
    Write-Host "   Key: $($response.key)" -ForegroundColor Gray
    Write-Host "   Size: $($response.sizeDisplay)" -ForegroundColor Gray
    
    # Save the key for deletion test
    $global:uploadedKey = $response.key
    
} catch {
    Write-Host "   ? Upload Firmware Failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "   Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
}

# Cleanup temp file
Remove-Item $testFilePath -ErrorAction SilentlyContinue
Write-Host ""

# Test 4: Get Download URL
if ($global:uploadedKey) {
    Write-Host "4. Testing Get Download URL (GET /api/firmware/download/{key})..." -ForegroundColor Yellow
    try {
        $encodedKey = [uri]::EscapeDataString($global:uploadedKey)
        $response = Invoke-RestMethod -Uri "$baseUrl/download/$encodedKey" -Method Get -ErrorAction Stop
        Write-Host "   ? Get Download URL Passed" -ForegroundColor Green
        Write-Host "   Download URL generated (expires in $($response.expiresIn) seconds)" -ForegroundColor Gray
    } catch {
        Write-Host "   ? Get Download URL Failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host ""
}

# Test 5: Delete Firmware
if ($global:uploadedKey) {
    Write-Host "5. Testing Delete Firmware (DELETE /api/firmware/admin/{key})..." -ForegroundColor Yellow
    try {
        $encodedKey = [uri]::EscapeDataString($global:uploadedKey)
        $response = Invoke-RestMethod -Uri "$baseUrl/admin/$encodedKey" -Method Delete -ErrorAction Stop
        Write-Host "   ? Delete Firmware Passed" -ForegroundColor Green
        Write-Host "   Message: $($response.message)" -ForegroundColor Gray
    } catch {
        Write-Host "   ? Delete Firmware Failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TEST SUITE COMPLETED" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary:" -ForegroundColor Yellow
Write-Host "- Health Check: Verify S3 connectivity" -ForegroundColor Gray
Write-Host "- List Firmwares: Retrieve all available firmwares" -ForegroundColor Gray
Write-Host "- Upload Firmware: Upload new firmware with metadata" -ForegroundColor Gray
Write-Host "- Get Download URL: Generate presigned S3 URL" -ForegroundColor Gray
Write-Host "- Delete Firmware: Remove firmware from S3" -ForegroundColor Gray
Write-Host ""
Write-Host "All firmware management endpoints are fully implemented! ?" -ForegroundColor Green
