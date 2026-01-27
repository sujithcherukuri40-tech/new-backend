# EC2 Migration Execution Guide

## ?? Goal
Run EF Core migrations from EC2 to create database schema in private RDS instance.

## ? Prerequisites Check

Before starting, verify:
- [ ] EC2 instance is running
- [ ] You have SSH key (.pem file)
- [ ] You know EC2 public IP address
- [ ] RDS security group allows EC2 ? RDS on port 5432
- [ ] appsettings.json has correct RDS connection string

---

## ?? Quick Start (Copy-Paste Commands)

### Step 1: Prepare Code on Local Machine

```powershell
# Navigate to your project
cd C:\Pavaman\config

# Ensure latest changes are committed
git add .
git commit -m "Add database entities and migrations"
git push origin main
```

### Step 2: SSH to EC2

```bash
# Replace with your actual key path and EC2 IP
ssh -i path/to/your-key.pem ec2-user@YOUR_EC2_PUBLIC_IP
```

---

## ?? One-Time EC2 Setup

Run these commands **once** on EC2:

```bash
#!/bin/bash

# 1. Install .NET 9 SDK
echo "Installing .NET 9 SDK..."
sudo dnf install -y dotnet-sdk-9.0

# 2. Verify .NET installation
dotnet --version

# 3. Install EF Core tools globally
echo "Installing EF Core tools..."
dotnet tool install --global dotnet-ef --version 9.0.0

# 4. Add tools to PATH
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc

# 5. Verify EF tools
dotnet ef --version

# 6. Install Git (if not already installed)
sudo dnf install -y git

# 7. Install PostgreSQL client for verification
sudo dnf install -y postgresql15

echo "? EC2 setup complete!"
```

---

## ?? Deploy Code to EC2

### Option 1: Clone from GitHub (Recommended)

```bash
# On EC2
cd ~
git clone https://github.com/sujithcherukuri40-tech/drone-config.git
cd drone-config

# Or if already cloned, just pull latest
cd ~/drone-config
git pull origin main
```

### Option 2: Transfer via SCP (Alternative)

```powershell
# On local machine
cd C:\Pavaman\config

# Create deployment package
dotnet publish PavamanDroneConfigurator.UI -c Release -o publish

# Transfer to EC2
scp -i path/to/key.pem -r publish ec2-user@EC2_IP:~/drone-config
```

---

## ??? Run Database Migration

```bash
# On EC2
cd ~/drone-config

# Restore NuGet packages (if needed)
dotnet restore

# Apply migration
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

### Expected Success Output

```
Build started...
Build succeeded.
Applying migration '20260127055501_InitialCreate'.
Done.
```

### If You See Errors

**Error: "Unable to resolve service for type 'DbContextOptions'"**
- Solution: Ensure appsettings.json is in the same directory as the .dll files

**Error: "Connection timeout"**
- Solution: Verify RDS security group allows EC2 security group on port 5432
- Verify: `psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"`

**Error: "Password authentication failed"**
- Solution: Check password in appsettings.json matches RDS user password

---

## ? Verify Migration Success

### Test 1: Check EF Migrations History

```bash
# On EC2
cd ~/drone-config

dotnet ef migrations list \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

**Expected:**
```
20260127055501_InitialCreate (Applied)
```

### Test 2: Connect to Database and List Tables

```bash
# On EC2
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"
```

**Inside psql:**
```sql
-- List all tables
\dt

-- Expected output:
--  Schema |         Name          | Type  |    Owner
-- --------+-----------------------+-------+--------------
--  public | __EFMigrationsHistory | table | new_app_user
--  public | calibration_records   | table | new_app_user
--  public | drones                | table | new_app_user
--  public | parameter_history     | table | new_app_user

-- Describe drones table
\d drones

-- Count rows (should be 0 initially)
SELECT COUNT(*) FROM drones;

-- Exit psql
\q
```

### Test 3: Verify Table Structure

```bash
# On EC2 - Quick verification script
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require" << EOF
-- Verify all expected tables exist
SELECT 
    table_name,
    (SELECT COUNT(*) FROM information_schema.columns WHERE table_name = t.table_name) as column_count
FROM information_schema.tables t
WHERE table_schema = 'public'
ORDER BY table_name;
EOF
```

**Expected:**
```
         table_name        | column_count
---------------------------+--------------
 __EFMigrationsHistory     |            2
 calibration_records       |            9
 drones                    |           10
 parameter_history         |            7
```

---

## ?? Rollback (If Needed)

If something goes wrong and you need to rollback:

```bash
# On EC2
cd ~/drone-config

# Rollback to initial state (removes all migrations)
dotnet ef database update 0 \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext

# Then re-apply
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

---

## ?? Complete Workflow

```bash
#!/bin/bash
# Complete migration workflow on EC2

echo "?? Starting database migration..."

# 1. Navigate to project
cd ~/drone-config

# 2. Pull latest code (if using Git)
echo "?? Pulling latest code..."
git pull origin main

# 3. Restore packages
echo "?? Restoring packages..."
dotnet restore

# 4. Apply migration
echo "??? Applying migration..."
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext

# 5. Verify
echo "? Verifying tables..."
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require" -c "\dt"

echo "? Migration complete!"
```

Save this as `migrate.sh`, make it executable, and run:

```bash
chmod +x migrate.sh
./migrate.sh
```

---

## ?? Success Criteria

? Migration completed with "Done." message
? Four tables exist in database:
   - `drones`
   - `parameter_history`
   - `calibration_records`
   - `__EFMigrationsHistory`
? `\dt` in psql shows all tables
? No error messages in migration output

---

## ?? Security Notes

- ? RDS remains **private** (172.31.10.17)
- ? Only EC2 can access RDS
- ? SSL/TLS encrypted connection
- ? Password in appsettings.json (temporary - move to Secrets Manager next)

---

## ?? Troubleshooting

### Issue: "dotnet: command not found"
```bash
# Install .NET SDK
sudo dnf install -y dotnet-sdk-9.0
```

### Issue: "dotnet ef: command not found"
```bash
# Install EF tools and add to PATH
dotnet tool install --global dotnet-ef --version 9.0.0
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc
```

### Issue: "Unable to find project file"
```bash
# Ensure you're in the right directory
cd ~/drone-config
ls -la PavamanDroneConfigurator.Infrastructure/
```

### Issue: "Connection timeout to RDS"
```bash
# Test basic connectivity
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"

# If this fails, check RDS security group
```

### Issue: "Build failed"
```bash
# Restore packages first
dotnet restore
dotnet build
```

---

## ?? Next Steps After Migration

1. ? **Verify migration successful** (all 4 tables exist)
2. ? **Test application connection** from EC2
3. ? **Move password to AWS Secrets Manager**
4. ? **Set up automated backups** for RDS
5. ? **Configure CloudWatch monitoring**

---

**Time Estimate:**
- First-time setup: ~15 minutes
- Subsequent migrations: ~2 minutes
