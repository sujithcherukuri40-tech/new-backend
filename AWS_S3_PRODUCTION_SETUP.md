# AWS S3 Integration - Production Setup Guide

## ?? Production Deployment on EC2

This guide covers the complete setup for AWS S3 integration for firmware files and parameter logs on EC2 with IAM roles.

---

## ?? Prerequisites

- EC2 instance running (Ubuntu/Amazon Linux recommended)
- S3 bucket: `drone-config-param-logs` in `ap-south-1` region
- IAM role attached to EC2 instance OR AWS CLI configured

---

## ?? Step 1: Create IAM Policy for S3 Access

### Policy Name: `DroneConfiguratorS3Access`

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
      "Sid": "FirmwareRead",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:GetObjectVersion"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs/firmwares/*"
    },
    {
      "Sid": "AdminFirmwareWrite",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs/firmwares/*"
    },
    {
      "Sid": "ParameterLogsWrite",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject"
      ],
      "Resource": "arn:aws:s3:::drone-config-param-logs/params-logs/*"
    }
  ]
}
```

### Create Policy (AWS CLI):

```bash
aws iam create-policy \
  --policy-name DroneConfiguratorS3Access \
  --policy-document file://s3-policy.json \
  --description "S3 access for Drone Configurator firmware and parameter logs"
```

---

## ?? Step 2: Create IAM Role for EC2

### Role Name: `DroneConfiguratorEC2Role`

```bash
# Create trust relationship for EC2
cat > ec2-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Service": "ec2.amazonaws.com"
      },
      "Action": "sts:AssumeRole"
    }
  ]
}
EOF

# Create IAM role
aws iam create-role \
  --role-name DroneConfiguratorEC2Role \
  --assume-role-policy-document file://ec2-trust-policy.json \
  --description "EC2 role for Drone Configurator with S3 access"

# Attach S3 policy to role
aws iam attach-role-policy \
  --role-name DroneConfiguratorEC2Role \
  --policy-arn arn:aws:iam::YOUR_ACCOUNT_ID:policy/DroneConfiguratorS3Access

# Create instance profile
aws iam create-instance-profile \
  --instance-profile-name DroneConfiguratorEC2Profile

# Add role to instance profile
aws iam add-role-to-instance-profile \
  --instance-profile-name DroneConfiguratorEC2Profile \
  --role-name DroneConfiguratorEC2Role
```

---

## ?? Step 3: Attach IAM Role to EC2 Instance

### Option A: Using AWS Console
1. Go to EC2 Dashboard
2. Select your instance
3. Actions ? Security ? Modify IAM role
4. Select `DroneConfiguratorEC2Role`
5. Save

### Option B: Using AWS CLI

```bash
# Get your EC2 instance ID
INSTANCE_ID="i-xxxxxxxxxxxxxxxxx"

# Attach IAM role
aws ec2 associate-iam-instance-profile \
  --instance-id $INSTANCE_ID \
  --iam-instance-profile Name=DroneConfiguratorEC2Profile
```

---

## ?? Step 4: Setup S3 Bucket Structure

```bash
# Verify bucket exists
aws s3 ls s3://drone-config-param-logs/

# Create folder structure
aws s3api put-object --bucket drone-config-param-logs --key firmwares/
aws s3api put-object --bucket drone-config-param-logs --key params-logs/

# Enable versioning (recommended)
aws s3api put-bucket-versioning \
  --bucket drone-config-param-logs \
  --versioning-configuration Status=Enabled

# Enable server-side encryption
aws s3api put-bucket-encryption \
  --bucket drone-config-param-logs \
  --server-side-encryption-configuration '{
    "Rules": [{
      "ApplyServerSideEncryptionByDefault": {
        "SSEAlgorithm": "AES256"
      }
    }]
  }'
```

---

## ?? Step 5: Upload Sample Firmware Files

```bash
# Upload firmware files to S3
# Files should be named to indicate vehicle type:
# - firmware-of-arducopter.apj (Copter)
# - firmware-of-plane.apj (Plane)
# - firmware-of-rover.apj (Rover)

aws s3 cp firmware-of-arducopter.apj s3://drone-config-param-logs/firmwares/
aws s3 cp firmware-of-plane.apj s3://drone-config-param-logs/firmwares/
aws s3 cp firmware-of-rover.apj s3://drone-config-param-logs/firmwares/

# Verify upload
aws s3 ls s3://drone-config-param-logs/firmwares/
```

---

## ?? Step 6: Backend API Configuration

### File: `PavamanDroneConfigurator.API/.env`

**PRODUCTION (EC2 with IAM Role):**

```sh
# NO AWS CREDENTIALS NEEDED - EC2 IAM Role provides them automatically
# AWS SDK will use EC2 instance metadata service

# Database Configuration
DB_HOST=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=your_db_user
DB_PASSWORD=your_secure_password
DB_SSL_MODE=Require

# JWT Configuration
JWT_SECRET_KEY=your_secure_random_key_minimum_32_characters_long
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient

# API Configuration
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000

# S3 Configuration (informational only - not required)
AWS_REGION=ap-south-1
S3_BUCKET_NAME=drone-config-param-logs
```

**DEVELOPMENT (Local with AWS CLI):**

```sh
# Use AWS CLI credentials
AWS_PROFILE=default
AWS_REGION=ap-south-1

# OR use explicit credentials (NOT RECOMMENDED)
# AWS_ACCESS_KEY_ID=AKIA...
# AWS_SECRET_ACCESS_KEY=...

# Rest same as production
DB_HOST=localhost
DB_PORT=5432
# ... other settings
```

---

## ??? Step 7: UI Configuration

### File: `PavamanDroneConfigurator.UI/.env`

```sh
# Backend API URL
API_BASE_URL=http://localhost:5000

# OR if API is on different server
# API_BASE_URL=http://your-ec2-ip:5000
# API_BASE_URL=https://api.yourdomain.com
```

---

## ?? Step 8: Test S3 Access

### On EC2 Instance:

```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@your-ec2-ip

# Test AWS CLI (should work without credentials)
aws s3 ls s3://drone-config-param-logs/

# Test .NET SDK access
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet run -- --test-s3
```

### Test Script (add to API for testing):

```bash
# In API directory
curl http://localhost:5000/api/firmware/health

# Should return:
# {"status":"healthy","message":"S3 bucket is accessible"}
```

---

## ?? Step 9: User ID and FC ID Format

For parameter logging, configure:

### User ID Format:
- **Email-based**: Use email as ID (e.g., `admin@pavaman.com`)
- **GUID**: Use user GUID from database (e.g., `550e8400-e29b-41d4-a716-446655440000`)
- **Username**: Simple username (e.g., `admin`)

### Flight Controller ID Format:
- **Serial Number**: Use FC serial/board ID (e.g., `CUBE001234`)
- **Board Type**: Use board type + serial (e.g., `CubeOrangePlus_1234`)
- **Custom**: Any unique identifier

### Example Parameter Log Path:
```
params-logs/
  ??? user_admin@pavaman.com/
      ??? drone_CUBE001234/
          ??? params_20250130_120000.csv
          ??? params_20250130_140000.csv
          ??? params_20250131_090000.csv
```

---

## ?? Step 10: Security Best Practices

### 1. S3 Bucket Policy (Block Public Access):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "DenyPublicAccess",
      "Effect": "Deny",
      "Principal": "*",
      "Action": "s3:*",
      "Resource": [
        "arn:aws:s3:::drone-config-param-logs",
        "arn:aws:s3:::drone-config-param-logs/*"
      ],
      "Condition": {
        "Bool": {
          "aws:SecureTransport": "false"
        }
      }
    }
  ]
}
```

### 2. Enable S3 Access Logging:

```bash
# Create logging bucket
aws s3 mb s3://drone-config-param-logs-access-logs

# Enable logging
aws s3api put-bucket-logging \
  --bucket drone-config-param-logs \
  --bucket-logging-status '{
    "LoggingEnabled": {
      "TargetBucket": "drone-config-param-logs-access-logs",
      "TargetPrefix": "access-logs/"
    }
  }'
```

### 3. Enable MFA Delete (Optional):

```bash
aws s3api put-bucket-versioning \
  --bucket drone-config-param-logs \
  --versioning-configuration Status=Enabled,MFADelete=Enabled \
  --mfa "arn:aws:iam::YOUR_ACCOUNT_ID:mfa/root-account-mfa-device XXXXXX"
```

---

## ?? Step 11: Deploy Application

```bash
# SSH to EC2
ssh -i your-key.pem ec2-user@your-ec2-ip

# Clone repository (if not already)
cd ~
git clone https://github.com/your-repo/drone-config.git
cd drone-config

# Pull latest changes
git pull origin main

# Build API
cd PavamanDroneConfigurator.API
dotnet restore
dotnet build -c Release

# Run database migrations
dotnet ef database update

# Start API
dotnet run -c Release

# Test S3 integration
curl http://localhost:5000/api/firmware/health
curl http://localhost:5000/api/firmware/inapp
```

---

## ?? Step 12: Verify Everything Works

### 1. Check S3 Access:
```bash
curl http://localhost:5000/api/firmware/health
# Expected: {"status":"healthy","message":"S3 bucket is accessible"}
```

### 2. List Firmwares:
```bash
curl http://localhost:5000/api/firmware/inapp
# Expected: JSON array with firmware metadata
```

### 3. Test Admin Upload:
```bash
curl -X POST http://localhost:5000/api/firmware/admin/upload \
  -F "file=@firmware-test.apj" \
  -F "customFileName=test-firmware.apj"
```

### 4. Test Parameter Logging:
```bash
# This will be tested through the UI when parameters change
# Check S3 after changing parameters in the app
aws s3 ls s3://drone-config-param-logs/params-logs/ --recursive
```

---

## ?? Troubleshooting

### Issue: "Access Denied" Error

**Solution:**
```bash
# Verify IAM role is attached
aws ec2 describe-instances --instance-ids i-xxxxx \
  --query 'Reservations[0].Instances[0].IamInstanceProfile'

# Verify role has correct policy
aws iam list-attached-role-policies --role-name DroneConfiguratorEC2Role

# Test credentials
aws sts get-caller-identity
```

### Issue: "Bucket Not Found"

**Solution:**
```bash
# Verify bucket exists in correct region
aws s3api get-bucket-location --bucket drone-config-param-logs

# List bucket contents
aws s3 ls s3://drone-config-param-logs/
```

### Issue: Application Can't Access S3

**Solution:**
```bash
# Check application logs
tail -f ~/drone-config/PavamanDroneConfigurator.API/logs/app.log

# Verify AWS SDK is using EC2 role
# Add logging to AwsS3Service constructor
```

---

## ?? Summary Checklist

- [ ] IAM policy created: `DroneConfiguratorS3Access`
- [ ] IAM role created: `DroneConfiguratorEC2Role`
- [ ] Instance profile created and attached to EC2
- [ ] S3 bucket exists: `drone-config-param-logs`
- [ ] S3 folder structure created: `firmwares/` and `params-logs/`
- [ ] Bucket versioning enabled
- [ ] Bucket encryption enabled
- [ ] Sample firmware files uploaded
- [ ] `.env` files configured (API and UI)
- [ ] API deployed and running
- [ ] S3 health check passes
- [ ] Firmware list endpoint returns data
- [ ] Admin upload works
- [ ] Parameter logging works

---

## ?? Next Steps

1. **UI Integration**: The UI will automatically load firmwares from S3 when "In-App (offline)" is selected
2. **Admin Panel**: Access admin firmware management at `/admin/firmware`
3. **Parameter Tracking**: Parameter changes are automatically logged to S3
4. **Monitoring**: Set up CloudWatch logs for S3 access patterns

---

## ?? Additional Resources

- [AWS IAM Roles for EC2](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/iam-roles-for-amazon-ec2.html)
- [AWS SDK for .NET](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/welcome.html)
- [S3 Best Practices](https://docs.aws.amazon.com/AmazonS3/latest/userguide/best-practices.html)

---

**Ready for Production!** ??
