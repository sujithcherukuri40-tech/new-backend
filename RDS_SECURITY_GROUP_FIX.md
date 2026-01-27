# ? CORRECT: Run Migrations from EC2 (Not Local Machine)

## ?? Architecture Understanding

### Your Current Setup (CORRECT)
```
Laptop (Public Internet)
    ? SSH
EC2 (Public Subnet) ? Has public IP, can SSH
    ? VPC Private Network
RDS (Private Subnet) ? Private IP: 172.31.10.17
```

**Key Facts:**
- ? RDS is **private by design** (172.31.10.17)
- ? RDS is **only accessible from VPC**
- ? EC2 can reach RDS (already verified with psql)
- ? Security groups are **correctly configured**
- ? Your laptop **cannot and should not** reach RDS directly

## ? Why the Previous Advice Was WRONG

The suggestion to add your laptop IP (`122.187.104.62/32`) to RDS security group would:
1. **Still fail** - Private IP (172.31.x.x) is not routable from public internet
2. **Require making RDS public** - Security anti-pattern
3. **Violate AWS best practices** - Databases should stay private

## ? The CORRECT Solution

**Run EF Core migrations FROM EC2 instance**

Why this works:
- ? EC2 is already inside the VPC
- ? Security group already allows EC2 ? RDS on port 5432
- ? SSL/TLS already configured
- ? Network path already exists
- ? You already proved psql works from EC2

---

## ?? Step-by-Step: Run Migrations from EC2

### Prerequisites
- EC2 instance with .NET 9 SDK installed
- Your project code deployed to EC2
- appsettings.json with correct connection string

### Step 1: Prepare Deployment Package

On your local machine, create a deployment package:

```powershell
# Navigate to solution root
cd C:\Pavaman\config

# Publish the project
dotnet publish PavamanDroneConfigurator.UI -c Release -o publish

# The publish folder will contain everything needed
```

### Step 2: Transfer Files to EC2

```powershell
# Using SCP to transfer files
# Replace <EC2-PUBLIC-IP> and <path-to-key.pem>
scp -i <path-to-key.pem> -r publish ec2-user@<EC2-PUBLIC-IP>:~/drone-configurator
```

**OR** use Git (if repository is up to date):

```bash
# On EC2
cd ~
git clone https://github.com/sujithcherukuri40-tech/drone-config.git
cd drone-config
```

### Step 3: SSH into EC2

```powershell
ssh -i <path-to-key.pem> ec2-user@<EC2-PUBLIC-IP>
```

### Step 4: Install .NET 9 SDK (if not already installed)

```bash
# On EC2 - Amazon Linux 2023
sudo dnf install -y dotnet-sdk-9.0

# Verify installation
dotnet --version
```

### Step 5: Install EF Core Tools

```bash
# On EC2
dotnet tool install --global dotnet-ef --version 9.0.0

# Add to PATH (if needed)
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
source ~/.bashrc

# Verify
dotnet ef --version
```

### Step 6: Run Migration from EC2

```bash
# Navigate to your project directory
cd ~/drone-config  # or ~/drone-configurator if using SCP

# Apply migration
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext
```

**Expected output:**
```
Build started...
Build succeeded.
Applying migration '20260127055501_InitialCreate'.
Done.
```

### Step 7: Verify Tables Created

```bash
# On EC2
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"

# Inside psql
\dt

# Expected output:
#  Schema |         Name          | Type  |    Owner
# --------+-----------------------+-------+--------------
#  public | __EFMigrationsHistory | table | new_app_user
#  public | calibration_records   | table | new_app_user
#  public | drones                | table | new_app_user
#  public | parameter_history     | table | new_app_user

\q
```

---

## ?? Security Best Practices (What You Did RIGHT)

? **RDS is private** - Not exposed to internet
? **Security group is locked** - Only EC2 can access
? **SSL/TLS enforced** - Encrypted connections
? **VPC isolation** - Network segmentation

**Keep it this way!** Don't make RDS public.

---

## ?? Alternative: SSH Tunnel (Optional Advanced Method)

If you **really** need to run migrations from your laptop (not recommended for production):

### Create SSH Tunnel
```powershell
# On your laptop
ssh -i <key.pem> -L 5432:drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com:5432 ec2-user@<EC2-IP> -N
```

### Then run migration using localhost
```powershell
# In a different terminal, update connection string temporarily
# Change Host to: localhost

dotnet ef database update --project PavamanDroneConfigurator.Infrastructure --startup-project PavamanDroneConfigurator.UI --context DroneDbContext
```

**Note:** This is more complex and not necessary. Running directly from EC2 is simpler.

---

## ?? Quick Reference Commands

### On Local Machine
```powershell
# Build and publish
dotnet publish PavamanDroneConfigurator.UI -c Release -o publish

# Transfer to EC2 (choose one method)
scp -i key.pem -r publish ec2-user@<EC2-IP>:~/drone-config
# OR
git push origin main  # Then pull on EC2
```

### On EC2
```bash
# One-time setup
sudo dnf install -y dotnet-sdk-9.0
dotnet tool install --global dotnet-ef --version 9.0.0
export PATH="$PATH:$HOME/.dotnet/tools"

# Get latest code
git clone https://github.com/sujithcherukuri40-tech/drone-config.git
cd drone-config

# Run migration
dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext

# Verify
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require" -c "\dt"
```

---

## ?? Summary

| Approach | Correct? | Why |
|----------|----------|-----|
| ? Add laptop IP to RDS SG | **NO** | Private RDS not routable from internet |
| ? Make RDS public | **NO** | Security anti-pattern |
| ? Run from EC2 | **YES** | EC2 already has VPC access |
| ?? SSH tunnel | **OPTIONAL** | Works but adds complexity |

**Recommended:** Run migrations from EC2 - it's simple, secure, and follows AWS best practices.

---

## ?? Next Steps

1. ? **SSH to EC2**
2. ? **Install .NET 9 SDK and EF tools**
3. ? **Clone/copy your project to EC2**
4. ? **Run `dotnet ef database update`**
5. ? **Verify tables with `\dt` in psql**
6. ? **Move to AWS Secrets Manager** (Step 2)

---

**Current Status**: Ready to run migration from EC2
**Blocked By**: Nothing - EC2 already has RDS access
**Time to Complete**: ~10 minutes (including .NET SDK install)

