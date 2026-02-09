# ?? How to Start the Backend API for S3 Firmware Integration

## **Problem**
The UI shows **"S3 unavailable"** because it cannot connect to the backend API.

## **IMPORTANT: Your API is on EC2, NOT localhost!**

Your backend API is running on AWS EC2 at: **`http://43.205.128.248:5000`**

---

## **Solution: Configure UI to Use EC2 API**

The UI needs to be configured to point to your EC2 instance instead of localhost.

### **Configuration Files to Update**

#### **1. Create/Update `.env` file in UI project:**

**File:** `C:\Pavaman\Final-repo\PavamanDroneConfigurator.UI\.env`

```sh
# Backend API URL - EC2 Instance
API_BASE_URL=http://43.205.128.248:5000
AUTH_API_URL=http://43.205.128.248:5000
AWS_API_URL=http://43.205.128.248:5000
```

#### **2. Create/Update `appsettings.json` in UI project:**

**File:** `C:\Pavaman\Final-repo\PavamanDroneConfigurator.UI\appsettings.json`

```json
{
  "Api": {
    "BaseUrl": "http://43.205.128.248:5000"
  },
  "Auth": {
    "ApiUrl": "http://43.205.128.248:5000",
    "UseAwsApi": true,
    "AwsApiUrl": "http://43.205.128.248:5000"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

---

## **Verify API is Running on EC2**

Before running the UI, verify the EC2 API is accessible:

### **1. Health Check**
```bash
curl http://43.205.128.248:5000/health
```

Expected response:
```json
{"status":"healthy","timestamp":"2025-01-30T..."}
```

### **2. S3 Firmware Health Check**
```bash
curl http://43.205.128.248:5000/api/firmware/health
```

Expected response:
```json
{"status":"healthy","message":"S3 bucket is accessible"}
```

### **3. List Firmwares from S3**
```bash
curl http://43.205.128.248:5000/api/firmware/inapp
```

Expected response (if firmwares exist in S3):
```json
[
  {
    "key": "firmwares/arducopter-cube.apj",
    "fileName": "arducopter-cube.apj",
    "displayName": "arducopter-cube",
    "vehicleType": "Copter",
    "size": 2453720,
    "sizeDisplay": "2.34 MB",
    "lastModified": "2025-01-30T...",
    "downloadUrl": "https://drone-config-param-logs.s3.ap-south-1.amazonaws.com/...",
    "firmwareName": "ArduCopter Stable",
    "firmwareVersion": "4.5.3",
    "firmwareDescription": "Latest stable release"
  }
]
```

Expected response (if S3 is empty):
```json
[]
```

---

## **If API is NOT Running on EC2**

If the API is not responding, you need to start it on your EC2 instance:

### **SSH into EC2:**
```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

### **Navigate to API directory:**
```bash
cd ~/drone-config/PavamanDroneConfigurator.API
# or wherever your API is deployed
```

### **Start the API:**
```bash
dotnet run --urls "http://0.0.0.0:5000"
```

Or use a process manager like `systemd` or `pm2`:

```bash
# Using systemd (recommended for production)
sudo systemctl start drone-configurator-api

# Or run in background with nohup
nohup dotnet run --urls "http://0.0.0.0:5000" > api.log 2>&1 &
```

---

## **Now Run the UI**

Once the configuration is updated and API is verified:

1. **Rebuild the UI project** (to pick up new configuration)
2. **Start the UI application**
3. **Navigate to Firmware Page**
4. **Select "In-App (offline)" firmware source**
5. **You should see:**
   - If firmwares exist in S3: `"S3: ArduCopter Stable v4.5.3 (2.34 MB)"`
   - If S3 is empty: `"No firmwares in S3"`
   - NO MORE: `"Using local firmware directory (S3 unavailable)"` ?

---

## **Network/Firewall Requirements**

### **EC2 Security Group Settings**

Ensure your EC2 instance's security group allows:

| Type | Protocol | Port | Source | Description |
|------|----------|------|--------|-------------|
| HTTP | TCP | 5000 | 0.0.0.0/0 | API access from anywhere |
| HTTP | TCP | 5000 | Your-IP/32 | API access from your IP only (more secure) |

To check/update security group:
```bash
# List security groups
aws ec2 describe-security-groups --group-ids sg-xxxxx

# Add inbound rule for port 5000
aws ec2 authorize-security-group-ingress \
  --group-id sg-xxxxx \
  --protocol tcp \
  --port 5000 \
  --cidr 0.0.0.0/0
```

### **Windows Firewall (if testing from Windows)**

Make sure Windows Firewall allows outbound connections to EC2:
```powershell
# Usually not needed, but if you have issues:
New-NetFirewallRule -DisplayName "Allow EC2 API" -Direction Outbound -Protocol TCP -RemoteAddress 43.205.128.248 -RemotePort 5000 -Action Allow
```

---

## **Troubleshooting**

### **Error: Connection refused / Timeout**

**Possible causes:**
1. API is not running on EC2
2. EC2 security group blocks port 5000
3. API is listening on 127.0.0.1 instead of 0.0.0.0

**Solution:**
```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Check if API is running
ps aux | grep dotnet

# Check what's listening on port 5000
sudo netstat -tlnp | grep 5000

# Restart API with correct binding
dotnet run --urls "http://0.0.0.0:5000"
```

### **Error: S3 bucket not accessible**

Check EC2 IAM role has S3 permissions:
```bash
# On EC2 instance
aws sts get-caller-identity
aws s3 ls s3://drone-config-param-logs/firmwares/
```

### **Error: CORS issues**

If you see CORS errors in browser console, the API's CORS policy needs to allow your IP.

**Fix in `Program.cs`:**
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDesktopApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

---

## **Production Deployment Checklist**

- [ ] EC2 instance is running
- [ ] API is deployed to EC2
- [ ] API is listening on `0.0.0.0:5000` (not `127.0.0.1`)
- [ ] EC2 security group allows inbound on port 5000
- [ ] EC2 IAM role has S3 access permissions
- [ ] S3 bucket `drone-config-param-logs` exists
- [ ] Firmwares are uploaded to S3 bucket
- [ ] UI configuration points to EC2 IP: `http://43.205.128.248:5000`
- [ ] API health endpoint responds: `curl http://43.205.128.248:5000/health`
- [ ] S3 firmware endpoint responds: `curl http://43.205.128.248:5000/api/firmware/inapp`

---

## **Summary**

The issue is that **the UI is trying to connect to `localhost:5000`** but your API is on **EC2 at `43.205.128.248:5000`**.

**Fix:**
1. ? Update UI configuration to use `http://43.205.128.248:5000`
2. ? Verify API is running and accessible on EC2
3. ? Ensure EC2 security group allows port 5000
4. ? Restart UI with new configuration
5. ? S3 firmwares will load correctly!

**Your S3 integration code is perfect - just needs correct API URL configuration!** ??
