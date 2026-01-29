# Create Admin User via SQL Script
# This script connects to your RDS database and creates the admin user

param(
    [string]$DbHost = "drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com",
    [string]$DbName = "drone_configurator",
    [string]$DbUser = "new_app_user",
    [string]$DbPassword = "Sujith2007"
)

Write-Host "?? Creating Admin User in RDS Database..." -ForegroundColor Cyan
Write-Host ""

# SQL script to create admin user
$sqlScript = @"
-- Check if user already exists
DO `$`$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM users WHERE email = 'admin@droneconfig.local') THEN
        -- Create admin user
        INSERT INTO users (
            id, 
            full_name, 
            email, 
            password_hash, 
            is_approved, 
            role, 
            created_at
        ) VALUES (
            gen_random_uuid(),
            'Admin User',
            'admin@droneconfig.local',
            '`$2a`$11`$vK3XqYQJ5jE7Y5rZ0wZ2HeO5xN7dZzYP7hK3L9mW8nC4qR6tS8vPe',
            true,
            'Admin',
            CURRENT_TIMESTAMP
        );
        
        RAISE NOTICE '? Admin user created successfully!';
        RAISE NOTICE 'Email: admin@droneconfig.local';
        RAISE NOTICE 'Password: Admin@123';
    ELSE
        RAISE NOTICE '??  Admin user already exists';
    END IF;
END `$`$;

-- Verify the user
SELECT email, full_name, is_approved, role, created_at 
FROM users 
WHERE email = 'admin@droneconfig.local';
"@

# Save SQL script to temp file
$tempSqlFile = "$env:TEMP\create_admin_user.sql"
$sqlScript | Out-File -FilePath $tempSqlFile -Encoding UTF8

Write-Host "?? SQL Script created at: $tempSqlFile" -ForegroundColor Yellow
Write-Host ""

# Check if psql is installed
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue

if (-not $psqlPath) {
    Write-Host "? PostgreSQL client (psql) not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install PostgreSQL client:" -ForegroundColor Yellow
    Write-Host "1. Download from: https://www.postgresql.org/download/windows/" -ForegroundColor White
    Write-Host "2. Or use the SQL script manually" -ForegroundColor White
    Write-Host ""
    Write-Host "Manual SQL Script:" -ForegroundColor Cyan
    Write-Host $sqlScript
    exit 1
}

Write-Host "? PostgreSQL client found: $($psqlPath.Source)" -ForegroundColor Green
Write-Host ""

# Connection string
$env:PGPASSWORD = $DbPassword

Write-Host "?? Connecting to RDS database..." -ForegroundColor Cyan
Write-Host "   Host: $DbHost" -ForegroundColor Gray
Write-Host "   Database: $DbName" -ForegroundColor Gray
Write-Host "   User: $DbUser" -ForegroundColor Gray
Write-Host ""

try {
    # Execute the SQL script
    $output = & psql -h $DbHost -U $DbUser -d $DbName -f $tempSqlFile 2>&1
    
    Write-Host $output
    Write-Host ""
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Admin user creation completed successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "?? Login Credentials:" -ForegroundColor Cyan
        Write-Host "   Email: admin@droneconfig.local" -ForegroundColor White
        Write-Host "   Password: Admin@123" -ForegroundColor White
        Write-Host ""
        Write-Host "??  IMPORTANT: Change this password after first login!" -ForegroundColor Yellow
    } else {
        Write-Host "? Failed to execute SQL script" -ForegroundColor Red
        Write-Host "Error code: $LASTEXITCODE" -ForegroundColor Red
    }
} catch {
    Write-Host "? Error connecting to database:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "1. Check if your IP is allowed in RDS security group" -ForegroundColor White
    Write-Host "2. Verify database credentials are correct" -ForegroundColor White
    Write-Host "3. Ensure RDS is publicly accessible (if connecting from outside VPC)" -ForegroundColor White
} finally {
    # Clean up
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "?? Test the login:" -ForegroundColor Cyan
Write-Host '   $body = ''{"email":"admin@droneconfig.local","password":"Admin@123"}''' -ForegroundColor Gray
Write-Host '   Invoke-WebRequest -Uri "http://43.205.128.248:5000/auth/login" -Method POST -Body $body -ContentType "application/json" -UseBasicParsing' -ForegroundColor Gray
