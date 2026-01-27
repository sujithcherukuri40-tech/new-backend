# EC2 Migration Quick Reference

## ?? Prerequisites

### Information You Need
- **EC2 Public IP**: `YOUR_EC2_PUBLIC_IP`
- **SSH Key Path**: `path/to/your-key.pem`
- **GitHub Repo**: https://github.com/sujithcherukuri40-tech/drone-config

### Before You Start
1. Ensure EC2 instance is running
2. Have your SSH key accessible
3. Latest code is pushed to GitHub

---

## ?? Quick Start - 3 Commands

```bash
# 1. SSH to EC2
ssh -i path/to/your-key.pem ec2-user@YOUR_EC2_PUBLIC_IP

# 2. Clone repository (first time only)
git clone https://github.com/sujithcherukuri40-tech/drone-config.git
cd drone-config

# 3. Run migration script
bash ec2-scripts/run-migration.sh
```

**That's it!** The script will:
- ? Install .NET 9 SDK (if needed)
- ? Install EF Core tools (if needed)
- ? Clone/update repository
- ? Restore packages
- ? Apply migration
- ? Verify tables created

---

## ?? Manual Step-by-Step (If Script Doesn't Work)

### Step 1: Connect to EC2

```bash
ssh -i path/to/your-key.pem ec2-user@YOUR_EC2_PUBLIC_IP
```

### Step 2: Install Prerequisites

```bash
# Install .NET 9 SDK
sudo dnf install -y dotnet-sdk-9.0

# Install EF Core tools
dotnet tool install --global dotnet-ef --version 9.0.0
export PATH="$PATH:$HOME/.dotnet/tools"
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc

# Verify installations
dotnet --version
dotnet ef --version
```

### Step 3: Get Code

```bash
# Clone repository (first time)
cd ~
git clone https://github.com/sujithcherukuri40-tech/drone-config.git
cd drone-config

# OR pull latest changes (subsequent times)
cd ~/drone-config
git pull origin main
```

### Step 4: Apply Migration

```bash
cd ~/drone-config

# Restore packages
dotnet restore

# Apply migration
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

### Step 5: Verify Success

```bash
# Check migrations
dotnet ef migrations list \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext

# Connect to database
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"

# List tables (in psql)
\dt
\q
```

---

## ? Success Output Examples

### Migration Success:
```
Build started...
Build succeeded.
Applying migration '20260127055501_InitialCreate'.
Done.
```

### Migrations List:
```
20260127055501_InitialCreate (Applied)
```

### Database Tables:
```
 Schema |         Name          | Type  |    Owner
--------+-----------------------+-------+--------------
 public | __EFMigrationsHistory | table | new_app_user
 public | calibration_records   | table | new_app_user
 public | drones                | table | new_app_user
 public | parameter_history     | table | new_app_user
```

---

## ?? Common Issues

### Issue: "dotnet: command not found"
```bash
sudo dnf install -y dotnet-sdk-9.0
```

### Issue: "dotnet-ef: command not found"
```bash
dotnet tool install --global dotnet-ef --version 9.0.0
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Issue: "Connection timeout to RDS"
```bash
# Verify EC2 can reach RDS
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"
# If this works, migration should work too
```

### Issue: "Build failed"
```bash
# Clean and restore
dotnet clean
dotnet restore
dotnet build
```

---

## ?? Future Migrations

After initial setup, running new migrations is simple:

```bash
# SSH to EC2
ssh -i key.pem ec2-user@EC2_IP

# Update code
cd ~/drone-config
git pull origin main

# Apply new migrations
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

---

## ?? Get Help

If you encounter errors:
1. Check the error message carefully
2. Verify prerequisites are installed
3. Ensure you're in the correct directory
4. Try the manual steps instead of the script
5. Check EC2 security group allows RDS access

---

## ?? Checklist

- [ ] SSH key accessible
- [ ] EC2 instance running
- [ ] Latest code pushed to GitHub
- [ ] Connected to EC2 via SSH
- [ ] .NET SDK installed
- [ ] EF Core tools installed
- [ ] Repository cloned/pulled
- [ ] Packages restored
- [ ] Migration applied
- [ ] Tables verified in database

---

**Time Estimate**: 10-15 minutes (first time), 2-3 minutes (subsequent)
