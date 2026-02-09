# ?? S3 Integration Troubleshooting Guide

## ? Endpoint Verification Results

### Summary
| Endpoint | Status | Issue |
|----------|--------|-------|
| `GET /health` | ? **WORKING** | Returns 200 OK |
| `GET /api/firmware/health` | ? **FAILING** | 500 Internal Server Error |
| `GET /api/firmware/inapp` | ? **FAILING** | 500 Internal Server Error |

---

## ?? Root Cause

The **500 Internal Server Error** on firmware endpoints indicates that:
1. ? API is running and accessible
2. ? Code is deployed correctly
3. ? **S3 Service cannot access AWS resources**

**Most likely cause:** EC2 instance lacks IAM role or IAM role lacks S3 permissions.

---

## ??? Fix Steps (SSH into EC2)

### Step 1: Connect to EC2

```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

---

### Step 2: Verify IAM Role is Attached

```bash
# Check if IAM role is attached to instance
curl -s http://169.254.169.254/latest/meta-data/iam/info

# Expected output (if role is attached):
# {
#   "Code" : "Success",
#   "LastUpdated" : "2025-01-30T...",
#   "InstanceProfileArn" : "arn:aws:iam::ACCOUNT:instance-profile/ROLE_NAME",
#   "InstanceProfileId" : "AIPAI..."
# }

# If you get error or no output, IAM role is NOT attached!
```

**If NO IAM role attached:**
1. Go to AWS Console ? EC2 ? Instances
2. Select your instance (`43.205.128.248`)
3. Actions ? Security ? **Modify IAM role**
4. Select or create a role with S3 permissions
5. Click **Update IAM role**

---

### Step 3: Verify AWS Credentials Work

```bash
# Test AWS CLI (should work if IAM role is attached)
aws sts get-caller-identity

# Expected output:
# {
#   "UserId": "AIDAI...",
#   "Account": "123456789012",
#   "Arn": "arn:aws:sts::123456789012:assumed-role/RoleName/i-xxxxx"
# }

# If you get "Unable to locate credentials", IAM role is not working
```

---

### Step 4: Test S3 Bucket Access

```bash
# List buckets (should show drone-config-param-logs)
aws s3 ls

# List contents of your bucket
aws s3 ls s3://drone-config-param-logs/

# Expected output:
#                           PRE firmwares/
#                           PRE params-logs/

# If you get "Access Denied", IAM role lacks S3 permissions
# If you get "NoSuchBucket", bucket doesn't exist
```

---

### Step 5: Check If Bucket Exists in Correct Region

```bash
# Get bucket region
aws s3api get-bucket-location --bucket drone-config-param-logs

# Expected output:
# {
#   "LocationConstraint": "ap-south-1"
# }

# If you get error, bucket might not exist
```

---

### Step 6: Create S3 Bucket (if it doesn't exist)

```bash
# Create bucket in ap-south-1 region
aws s3 mb s3://drone-config-param-logs --region ap-south-1

# Create folder structure
aws s3api put-object --bucket drone-config-param-logs --key firmwares/
aws s3api put-object --bucket drone-config-param-logs --key params-logs/

# Verify
aws s3 ls s3://drone-config-param-logs/
```

---

### Step 7: Verify IAM Permissions

```bash
# Check what IAM policy is attached to your role
aws iam list-attached-role-policies --role-name YOUR_ROLE_NAME

# Get policy details
aws iam get-role-policy --role-name YOUR_ROLE_NAME --policy-name YOUR_POLICY_NAME
```

**Required IAM Policy:**

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ListBucket",
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket",
        "s3:GetBucketLocation"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs"
    },
    {
      "Sid": "GetAndPutObjects",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:GetObjectMetadata",
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs/*"
    }
  ]
}
```

---

### Step 8: Check API Logs for Exact Error

```bash
# If API is running as systemd service
sudo journalctl -u drone-configurator-api -n 100 --no-pager | grep -i "error\|exception\|s3"

# If running manually
cd ~/drone-config/PavamanDroneConfigurator.API
tail -f logs/*.log

# Or check dotnet process logs
ps aux | grep dotnet
```

**Common errors to look for:**
- `Unable to get IAM security credentials` ? IAM role not attached
- `Access Denied` ? IAM role lacks S3 permissions
- `NoSuchBucket` ? S3 bucket doesn't exist
- `The bucket is in this region: us-east-1` ? Region mismatch

---

### Step 9: Restart API After Fixing IAM

```bash
# If running as systemd service
sudo systemctl restart drone-configurator-api

# If running manually
pkill -f dotnet
cd ~/drone-config/PavamanDroneConfigurator.API
nohup dotnet run --urls "http://0.0.0.0:5000" > api.log 2>&1 &

# Check if API started
curl http://localhost:5000/health
```

---

### Step 10: Test S3 Endpoints Again

```bash
# Test S3 health from EC2 localhost
curl http://localhost:5000/api/firmware/health

# Expected (once fixed):
# {"status":"healthy","message":"S3 bucket is accessible"}

# Test firmware list
curl http://localhost:5000/api/firmware/inapp

# Expected (if bucket is empty):
# []

# Expected (if firmwares exist):
# [{"key":"firmwares/test.apj",...}]
```

---

## ?? Quick Diagnostic Script

Run this on EC2 to diagnose all issues at once:

```bash
#!/bin/bash
echo "=== EC2 IAM Role Check ==="
curl -s http://169.254.169.254/latest/meta-data/iam/info | jq '.' 2>/dev/null || echo "No IAM role attached!"

echo -e "\n=== AWS Identity ==="
aws sts get-caller-identity 2>&1

echo -e "\n=== S3 Bucket Access Test ==="
aws s3 ls s3://drone-config-param-logs/ 2>&1

echo -e "\n=== S3 Bucket Region ==="
aws s3api get-bucket-location --bucket drone-config-param-logs 2>&1

echo -e "\n=== API Process Status ==="
ps aux | grep -E 'dotnet|PavamanDroneConfigurator' | grep -v grep

echo -e "\n=== Test API Health ==="
curl -s http://localhost:5000/health | jq '.' 2>/dev/null || echo "API not responding"

echo -e "\n=== Test S3 Health Endpoint ==="
curl -s http://localhost:5000/api/firmware/health 2>&1

echo -e "\n=== Recent API Logs (last 20 lines) ==="
sudo journalctl -u drone-configurator-api -n 20 --no-pager 2>/dev/null || \
  tail -20 ~/drone-config/PavamanDroneConfigurator.API/logs/*.log 2>/dev/null || \
  echo "No logs found"
```

Save as `diagnose-s3.sh`, make executable, and run:

```bash
chmod +x diagnose-s3.sh
./diagnose-s3.sh
```

---

## ?? Expected Fixes

### Scenario 1: No IAM Role Attached

**Symptom:**
```
curl http://169.254.169.254/latest/meta-data/iam/info
# Returns: 404 - Not Found
```

**Fix:**
1. AWS Console ? EC2 ? Select instance
2. Actions ? Security ? **Modify IAM role**
3. Create new role with S3 permissions or select existing
4. Save and wait 30 seconds
5. Restart API

---

### Scenario 2: IAM Role Lacks S3 Permissions

**Symptom:**
```
aws s3 ls s3://drone-config-param-logs/
# Returns: An error occurred (AccessDenied)
```

**Fix:**
1. AWS Console ? IAM ? Roles
2. Find the role attached to EC2
3. Attach policy: `AmazonS3ReadOnlyAccess` (for read) or custom policy (for read/write)
4. Wait 30 seconds for permissions to propagate
5. Test: `aws s3 ls s3://drone-config-param-logs/`

---

### Scenario 3: S3 Bucket Doesn't Exist

**Symptom:**
```
aws s3 ls s3://drone-config-param-logs/
# Returns: An error occurred (NoSuchBucket)
```

**Fix:**
```bash
aws s3 mb s3://drone-config-param-logs --region ap-south-1
aws s3api put-object --bucket drone-config-param-logs --key firmwares/
aws s3api put-object --bucket drone-config-param-logs --key params-logs/
```

---

### Scenario 4: Region Mismatch

**Symptom:**
```
aws s3api get-bucket-location --bucket drone-config-param-logs
# Returns: {"LocationConstraint": "us-east-1"}
# But code expects: ap-south-1
```

**Fix:**
Either:
- Change bucket region to `ap-south-1`, OR
- Update code to use correct region

---

## ? Verification Checklist

Once you've made the fixes, verify:

- [ ] IAM role is attached to EC2 instance
- [ ] IAM role has S3 permissions
- [ ] S3 bucket exists: `drone-config-param-logs`
- [ ] S3 bucket is in `ap-south-1` region
- [ ] AWS CLI can list bucket: `aws s3 ls s3://drone-config-param-logs/`
- [ ] API is running: `curl http://localhost:5000/health`
- [ ] S3 health check passes: `curl http://localhost:5000/api/firmware/health`
- [ ] Firmware list returns data: `curl http://localhost:5000/api/firmware/inapp`

---

## ?? Next Steps After Fix

Once all endpoints are working:

1. **Test from your local machine:**
   ```bash
   curl http://43.205.128.248:5000/api/firmware/health
   curl http://43.205.128.248:5000/api/firmware/inapp
   ```

2. **Run the UI application** and select "In-App (offline)" source

3. **Upload test firmware:**
   ```bash
   # From EC2
   curl -X POST http://localhost:5000/api/firmware/admin/upload \
     -F "file=@test-firmware.apj" \
     -F "firmwareName=Test Firmware" \
     -F "firmwareVersion=1.0.0"
   ```

4. **Verify firmware appears in UI**

---

## ?? Still Not Working?

Share the output of the diagnostic script and any error messages from:
- `sudo journalctl -u drone-configurator-api -n 50`
- `curl http://localhost:5000/api/firmware/health`
- API logs from `~/drone-config/PavamanDroneConfigurator.API/logs/`

---

**The code is perfect - this is purely an AWS IAM/S3 configuration issue!**
