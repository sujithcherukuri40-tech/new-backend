# EC2 Deployment Status Checker
# Run this from Windows PowerShell to check if EC2 deployment is needed

Write-Host "?? Checking EC2 API Status..." -ForegroundColor Yellow
Write-Host ""

$apiUrl = "http://43.205.128.248:5000"

# Test main health endpoint
Write-Host "1?? Testing main health endpoint..." -ForegroundColor Cyan
try {
    $health = Invoke-RestMethod -Uri "$apiUrl/health" -Method Get -TimeoutSec 5
    Write-Host "   ? API is running" -ForegroundColor Green
    Write-Host "   Response: $($health | ConvertTo-Json -Compress)" -ForegroundColor Gray
} catch {
    Write-Host "   ? API is not responding" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Test S3 firmware health endpoint
Write-Host "2?? Testing S3 firmware health endpoint..." -ForegroundColor Cyan
try {
    $s3Health = Invoke-RestMethod -Uri "$apiUrl/api/firmware/health" -Method Get -TimeoutSec 5
    Write-Host "   ? S3 integration is working!" -ForegroundColor Green
    Write-Host "   Response: $($s3Health | ConvertTo-Json -Compress)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "   ?? AWS SDK fix is deployed successfully!" -ForegroundColor Green
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    
    if ($statusCode -eq 500) {
        Write-Host "   ? 500 Internal Server Error - AWS SDK issue NOT fixed yet" -ForegroundColor Red
        Write-Host ""
        Write-Host "   ?? Action Required:" -ForegroundColor Yellow
        Write-Host "      1. SSH into EC2: ssh -i your-key.pem ec2-user@43.205.128.248" -ForegroundColor White
        Write-Host "      2. Run the quick deploy script from QUICK_DEPLOY_EC2.md" -ForegroundColor White
        Write-Host "      3. Or manually run:" -ForegroundColor White
        Write-Host "         cd ~/drone-config && git pull origin main" -ForegroundColor Gray
        Write-Host "         cd PavamanDroneConfigurator.API" -ForegroundColor Gray
        Write-Host "         dotnet restore --force --no-cache" -ForegroundColor Gray
        Write-Host "         dotnet publish -c Release -o /home/ec2-user/drone-api-published" -ForegroundColor Gray
        Write-Host "         sudo systemctl restart drone-api" -ForegroundColor Gray
    }
    elseif ($statusCode -eq 503) {
        Write-Host "   ??  S3 bucket not accessible (IAM/permissions issue)" -ForegroundColor Yellow
        Write-Host "   But AWS SDK fix is deployed (no more 500 error)" -ForegroundColor Green
    }
    else {
        Write-Host "   ? Error: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Status Code: $statusCode" -ForegroundColor Red
    }
}

Write-Host ""

# Test firmware list endpoint
Write-Host "3?? Testing firmware list endpoint..." -ForegroundColor Cyan
try {
    $firmwares = Invoke-RestMethod -Uri "$apiUrl/api/firmware/inapp" -Method Get -TimeoutSec 5
    Write-Host "   ? Firmware list endpoint is working!" -ForegroundColor Green
    
    if ($firmwares.Count -eq 0) {
        Write-Host "   ??  No firmwares in S3 bucket (bucket is empty)" -ForegroundColor Cyan
        Write-Host "   Upload firmwares via Admin Panel to populate the list" -ForegroundColor Gray
    } else {
        Write-Host "   ? Found $($firmwares.Count) firmware(s) in S3:" -ForegroundColor Green
        foreach ($fw in $firmwares) {
            Write-Host "      - $($fw.displayName) ($($fw.sizeDisplay))" -ForegroundColor Gray
        }
    }
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    
    if ($statusCode -eq 500) {
        Write-Host "   ? 500 Error - AWS SDK issue NOT fixed" -ForegroundColor Red
    } else {
        Write-Host "   ? Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "???????????????????????????????????????????????????" -ForegroundColor Gray

# Summary
Write-Host ""
Write-Host "?? Summary:" -ForegroundColor Cyan
Write-Host ""

try {
    $health = Invoke-RestMethod -Uri "$apiUrl/health" -Method Get -TimeoutSec 5 -ErrorAction Stop
    Write-Host "   API Status: ? Running" -ForegroundColor Green
} catch {
    Write-Host "   API Status: ? Not Running" -ForegroundColor Red
    exit
}

try {
    $s3Health = Invoke-RestMethod -Uri "$apiUrl/api/firmware/health" -Method Get -TimeoutSec 5 -ErrorAction Stop
    Write-Host "   S3 Integration: ? Working" -ForegroundColor Green
    Write-Host "   AWS SDK Fix: ? Deployed" -ForegroundColor Green
    Write-Host ""
    Write-Host "   ?? All systems operational!" -ForegroundColor Green
    Write-Host "   You can now use the Firmware Page in the UI." -ForegroundColor Green
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    
    if ($statusCode -eq 500) {
        Write-Host "   S3 Integration: ? Not Working (500 Error)" -ForegroundColor Red
        Write-Host "   AWS SDK Fix: ? NOT Deployed" -ForegroundColor Red
        Write-Host ""
        Write-Host "   ??  Deployment Required!" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "   Next Steps:" -ForegroundColor Cyan
        Write-Host "   1. Open a new PowerShell window" -ForegroundColor White
        Write-Host "   2. SSH into EC2:" -ForegroundColor White
        Write-Host "      ssh -i your-key.pem ec2-user@43.205.128.248" -ForegroundColor Gray
        Write-Host "   3. Run quick deploy:" -ForegroundColor White
        Write-Host "      cd ~/drone-config && git pull origin main && \\" -ForegroundColor Gray
        Write-Host "      cd PavamanDroneConfigurator.API && \\" -ForegroundColor Gray
        Write-Host "      dotnet restore --force --no-cache && \\" -ForegroundColor Gray
        Write-Host "      dotnet publish -c Release -o ~/drone-api-published && \\" -ForegroundColor Gray
        Write-Host "      sudo systemctl restart drone-api" -ForegroundColor Gray
        Write-Host ""
        Write-Host "   4. Run this script again to verify" -ForegroundColor White
    }
    elseif ($statusCode -eq 503) {
        Write-Host "   S3 Integration: ??  IAM/Permissions Issue" -ForegroundColor Yellow
        Write-Host "   AWS SDK Fix: ? Deployed" -ForegroundColor Green
        Write-Host ""
        Write-Host "   Next Steps:" -ForegroundColor Cyan
        Write-Host "   - Attach IAM role to EC2 instance with S3 permissions" -ForegroundColor White
        Write-Host "   - Create S3 bucket: drone-config-param-logs" -ForegroundColor White
        Write-Host "   - See S3_TROUBLESHOOTING.md for details" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "???????????????????????????????????????????????????" -ForegroundColor Gray
Write-Host ""
