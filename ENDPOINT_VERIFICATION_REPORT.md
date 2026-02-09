# ?? API Endpoint Verification Report

**Generated:** 2025-01-30  
**API Base URL:** `http://43.205.128.248:5000`

---

## ? Working Endpoints

### 1. Health Check Endpoint
```
GET http://43.205.128.248:5000/health
```

**Status:** ? **WORKING**

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2026-02-09T05:56:09.3721465Z"
}
```

**HTTP Status Code:** `200 OK`

---

## ? Failing Endpoints

### 2. S3 Firmware Health Check
```
GET http://43.205.128.248:5000/api/firmware/health
```

**Status:** ? **FAILING**

**Error:** `500 Internal Server Error`

**Possible Causes:**
1. **AWS Credentials Not Configured** - EC2 instance doesn't have IAM role attached
2. **S3 Bucket Not Accessible** - Bucket doesn't exist or IAM role lacks permissions
3. **AWS SDK Not Initialized** - Missing AWS configuration
4. **Region Mismatch** - Bucket is in different region than configured

---

### 3. List Firmwares from S3
```
GET http://43.205.128.248:5000/api/firmware/inapp
```

**Status:** ? **FAILING**

**Error:** `500 Internal Server Error`

**Possible Causes:**
- Same as above (S3 service initialization failure)
- The `AwsS3Service` is not properly initialized
- Missing IAM permissions for `s3:ListBucket` and `s3:GetObject`

---

## ?? Root Cause Analysis

The API is **running and accessible** (health endpoint works), but the **S3 integration is failing**.

### Most Likely Issues:

#### **1. EC2 IAM Role Not Attached or Lacks Permissions**

The EC2 instance needs an IAM role with S3 access permissions.

**Check IAM Role (SSH into EC2):**
```bash
ssh -i your-key.pem ec2-user@43.205.128.248

# Check if IAM role is attached
curl -s http://169.254.169.254/latest/meta-data/iam/info

# Test S3 access
aws s3 ls s3://drone-config-param-logs/

# Check current identity
aws sts get-caller-identity
```

**Expected IAM Policy:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket",
        "s3:GetBucketLocation"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs"
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:GetObjectMetadata"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs/firmwares/*"
    }
  ]
}
```

---

#### **2. S3 Bucket Doesn't Exist**

**Verify S3 Bucket (from EC2):**
```bash
aws s3 ls s3://drone-config-param-logs/
```

**Expected output:**
```
PRE firmwares/
PRE params-logs/
```

**If bucket doesn't exist, create it:**
```bash
aws s3 mb s3://drone-config-param-logs --region ap-south-1
aws s3api put-object --bucket drone-config-param-logs --key firmwares/
aws s3api put-object --bucket drone-config-param-logs --key params-logs/
```

---

#### **3. AWS Region Configuration Issue**

The `AwsS3Service` is configured for `ap-south-1` region.

**Check in code:**
```csharp
// In AwsS3Service.cs
_s3Client = new AmazonS3Client(Amazon.RegionEndpoint.APSouth1);
```

**Verify bucket region:**
```bash
aws s3api get-bucket-location --bucket drone-config-param-logs
```

Expected output: `{"LocationConstraint": "ap-south-1"}`

---

#### **4. Application Logs Show Error Details**

**Check API logs on EC2:**
```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Check application logs
journalctl -u drone-configurator-api -n 100 --no-pager

# Or if running manually
tail -f ~/drone-config/PavamanDroneConfigurator.API/logs/app.log
```

**Look for errors like:**
- `Unable to get IAM security credentials from EC2 Instance Metadata Service`
- `Access Denied`
- `The specified bucket does not exist`
- `UnauthorizedAccessException`

---

## ??? Fix Steps

### **Step 1: Verify EC2 IAM Role**

```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Check IAM role is attached
curl http://169.254.169.254/latest/meta-data/iam/info

# If no role attached, attach one from AWS Console:
# EC2 ? Select Instance ? Actions ? Security ? Modify IAM role
```

### **Step 2: Verify S3 Access**

```bash
# Test S3 CLI access
aws sts get-caller-identity
aws s3 ls s3://drone-config-param-logs/

# If you see "Access Denied", the IAM role needs S3 permissions
```

### **Step 3: Check API Logs**

```bash
# View recent logs
journalctl -u drone-configurator-api -n 50

# Or check application logs
cd ~/drone-config/PavamanDroneConfigurator.API
ls -la logs/
tail -f logs/app-*.log
```

### **Step 4: Restart API with Logging**

```bash
# Stop current API
sudo systemctl stop drone-configurator-api

# Run manually to see errors
cd ~/drone-config/PavamanDroneConfigurator.API
ASPNETCORE_ENVIRONMENT=Production dotnet run --urls "http://0.0.0.0:5000"

# Watch for S3-related errors when it starts
```

### **Step 5: Test S3 Health Endpoint Again**

```bash
# From your local machine
curl http://43.205.128.248:5000/api/firmware/health

# Expected (once fixed):
# {"status":"healthy","message":"S3 bucket is accessible"}
```

---

## ?? Endpoint Test Checklist

| Endpoint | URL | Status | Error |
|----------|-----|--------|-------|
| ? API Health | `GET /health` | **WORKING** | - |
| ? S3 Health | `GET /api/firmware/health` | **500 ERROR** | Internal Server Error |
| ? List Firmwares | `GET /api/firmware/inapp` | **500 ERROR** | Internal Server Error |
| ? Upload Firmware | `POST /api/firmware/admin/upload` | **NOT TESTED** | Requires authentication |
| ? Delete Firmware | `DELETE /api/firmware/admin/{key}` | **NOT TESTED** | Requires authentication |

---

## ?? Next Actions

### **Immediate Actions (Required):**

1. **SSH into EC2 instance**
2. **Check if IAM role is attached** - This is the most likely issue
3. **Verify S3 bucket exists** in `ap-south-1` region
4. **Check API logs** for specific error messages
5. **Attach correct IAM role** if missing

### **Once Fixed, Verify:**

```bash
# All these should return 200 OK
curl http://43.205.128.248:5000/health
curl http://43.205.128.248:5000/api/firmware/health
curl http://43.205.128.248:5000/api/firmware/inapp
```

---

## ?? Quick Diagnosis Commands

Run these on EC2 to quickly diagnose the issue:

```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# One-liner diagnosis
echo "=== IAM Role Check ===" && \
curl -s http://169.254.169.254/latest/meta-data/iam/info && \
echo -e "\n\n=== AWS Identity ===" && \
aws sts get-caller-identity && \
echo -e "\n\n=== S3 Bucket Access ===" && \
aws s3 ls s3://drone-config-param-logs/ && \
echo -e "\n\n=== API Process ===" && \
ps aux | grep dotnet
```

---

## ?? Summary

| Component | Status | Issue |
|-----------|--------|-------|
| API Server | ? Running | Accessible on port 5000 |
| Health Endpoint | ? Working | Returns 200 OK |
| S3 Service | ? Failed | 500 Internal Server Error |
| IAM Role | ? Unknown | Needs verification |
| S3 Bucket | ? Unknown | Needs verification |

**Conclusion:** The API infrastructure is working, but **S3 integration is failing due to AWS credentials/permissions issue**.

**Most likely fix:** Attach IAM role with S3 permissions to EC2 instance.

---

**Next Step:** SSH into EC2 and run the diagnosis commands above to identify the exact issue.
