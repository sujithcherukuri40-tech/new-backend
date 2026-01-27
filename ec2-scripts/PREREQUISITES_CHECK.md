# EC2 Prerequisites Verification Checklist

## Purpose
Before running database migrations, verify that EC2 instance has all required software and access.

---

## ? Verification Steps

Run these commands on your EC2 instance to verify readiness:

### 1. Check .NET SDK

```bash
dotnet --version
```

**Expected**: `9.0.x` or similar
**If missing**: 
```bash
sudo dnf install -y dotnet-sdk-9.0
```

### 2. Check EF Core Tools

```bash
dotnet ef --version
```

**Expected**: `Entity Framework Core .NET Command-line Tools 9.0.0`
**If missing**:
```bash
dotnet tool install --global dotnet-ef --version 9.0.0
export PATH="$PATH:$HOME/.dotnet/tools"
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc
```

### 3. Check Git

```bash
git --version
```

**Expected**: `git version 2.x.x`
**If missing**:
```bash
sudo dnf install -y git
```

### 4. Check PostgreSQL Client (Optional)

```bash
psql --version
```

**Expected**: `psql (PostgreSQL) 15.x`
**If missing**:
```bash
sudo dnf install -y postgresql15
```

### 5. Verify RDS Connectivity

```bash
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"
```

**Expected**: Password prompt
**If timeout**: Check EC2 security group allows outbound to RDS
**If authentication fails**: Verify password

### 6. Check Disk Space

```bash
df -h
```

**Expected**: At least 1GB free in home directory
**If low**: Clean up unnecessary files

### 7. Check Network Connectivity to GitHub

```bash
ping -c 3 github.com
```

**Expected**: Successful pings
**If fails**: Check EC2 internet gateway configuration

### 8. Verify EC2 Instance Type

```bash
curl http://169.254.169.254/latest/meta-data/instance-type
```

**Recommended**: t2.micro or better
**Minimum**: 1 vCPU, 1GB RAM

---

## ?? Security Verification

### Check EC2 Security Group

**Outbound Rules:**
- [ ] Allow HTTPS (443) to 0.0.0.0/0 (for package downloads)
- [ ] Allow PostgreSQL (5432) to RDS security group or VPC CIDR
- [ ] Allow DNS (53) to 0.0.0.0/0

**Inbound Rules:**
- [ ] Allow SSH (22) from your IP (for management)

### Check RDS Security Group

**Inbound Rules:**
- [ ] Allow PostgreSQL (5432) from EC2 security group
- [ ] **DO NOT** allow from 0.0.0.0/0

---

## ?? Pre-Migration Checklist

Before running migration:

- [ ] .NET SDK 9.0 installed
- [ ] EF Core tools installed
- [ ] Git installed
- [ ] Can connect to RDS with psql
- [ ] Repository cloned to EC2
- [ ] appsettings.json has correct connection string
- [ ] At least 1GB free disk space
- [ ] Internet connectivity working
- [ ] EC2 can reach GitHub
- [ ] EC2 security group allows outbound PostgreSQL

---

## ?? Ready to Migrate?

If all checks pass, you're ready to run:

```bash
cd ~/drone-config
bash ec2-scripts/run-migration.sh
```

---

## ?? Troubleshooting

### Can't Install Packages

```bash
# Update DNF cache
sudo dnf clean all
sudo dnf makecache

# Try installing again
sudo dnf install -y dotnet-sdk-9.0
```

### Can't Connect to RDS

```bash
# Check VPC DNS
nslookup drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com

# Test TCP connection
telnet drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com 5432
```

### Out of Disk Space

```bash
# Clean DNF cache
sudo dnf clean all

# Remove old kernels
sudo dnf remove $(dnf repoquery --installonly --latest-limit=-1 -q)

# Clean old logs
sudo journalctl --vacuum-time=7d
```

---

## ? Quick Validation Script

Copy and run this to validate everything at once:

```bash
#!/bin/bash

echo "=== EC2 Prerequisites Check ==="
echo ""

# .NET SDK
if command -v dotnet &> /dev/null; then
    echo "? .NET SDK: $(dotnet --version)"
else
    echo "? .NET SDK: NOT FOUND"
fi

# EF Core tools
if command -v dotnet-ef &> /dev/null; then
    echo "? EF Core: $(dotnet ef --version | head -n 1)"
else
    echo "? EF Core tools: NOT FOUND"
fi

# Git
if command -v git &> /dev/null; then
    echo "? Git: $(git --version)"
else
    echo "? Git: NOT FOUND"
fi

# PostgreSQL client
if command -v psql &> /dev/null; then
    echo "? PostgreSQL client: $(psql --version)"
else
    echo "! PostgreSQL client: NOT FOUND (optional)"
fi

# Disk space
echo "? Disk space: $(df -h ~ | tail -1 | awk '{print $4}') available"

# Internet
if ping -c 1 github.com &> /dev/null; then
    echo "? Internet: Connected"
else
    echo "? Internet: NOT CONNECTED"
fi

# RDS connectivity (requires password)
echo ""
echo "Testing RDS connectivity (enter password when prompted)..."
PGPASSWORD=Sujith2007 psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com -p 5432 -U new_app_user -d drone_configurator -c "SELECT 1" 2>&1 | grep -q "1" && echo "? RDS: Connected" || echo "? RDS: Connection failed"

echo ""
echo "=== Check Complete ==="
```

Save as `check-prerequisites.sh` and run:
```bash
bash check-prerequisites.sh
```

---

**Last Updated**: January 27, 2026
**Status**: Ready for verification
