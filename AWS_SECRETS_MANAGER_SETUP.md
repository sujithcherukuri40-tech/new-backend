# AWS Secrets Manager Setup Guide

## Prerequisites

1. AWS CLI installed
2. AWS credentials configured
3. Proper IAM permissions to access Secrets Manager

## Step 1: Configure AWS Credentials

### Using AWS CLI:
```powershell
aws configure
```

Enter:
- **AWS Access Key ID**: Your access key
- **AWS Secret Access Key**: Your secret key
- **Default region**: ap-south-1
- **Default output format**: json

### Or set environment variables:
```powershell
$env:AWS_ACCESS_KEY_ID="your-access-key"
$env:AWS_SECRET_ACCESS_KEY="your-secret-key"
$env:AWS_REGION="ap-south-1"
```

## Step 2: Store Secrets in AWS Secrets Manager

### Database Secret (JSON format):
```powershell
aws secretsmanager put-secret-value `
  --secret-id drone-configurator/postgres `
  --secret-string '{\"host\":\"drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com\",\"port\":\"5432\",\"database\":\"drone_configurator\",\"username\":\"new_app_user\",\"password\":\"Sujith2007\"}'
```

### JWT Secret (Plain text):
```powershell
aws secretsmanager put-secret-value `
  --secret-id drone-configurator/jwt-secret `
  --secret-string "kZx9mP2qR7tY4wV8nB3cF6hJ1lN5oS0uA9dG2kM5pQ8rT7vW4xE1yH6jL3nP0sU"
```

## Step 3: Verify Secrets

```powershell
# Verify database secret
aws secretsmanager get-secret-value --secret-id drone-configurator/postgres

# Verify JWT secret
aws secretsmanager get-secret-value --secret-id drone-configurator/jwt-secret
```

## Step 4: Update .env File (Development Fallback)

Add AWS configuration to your `.env` file:

```sh
# AWS Secrets Manager Configuration
AWS_REGION=ap-south-1
AWS_SECRETS_MANAGER_DB_SECRET=drone-configurator/postgres
AWS_SECRETS_MANAGER_JWT_SECRET=drone-configurator/jwt-secret

# Optional: AWS Credentials (if not using AWS CLI profile)
# AWS_ACCESS_KEY_ID=your-access-key
# AWS_SECRET_ACCESS_KEY=your-secret-key
```

## Configuration Priority

The application uses this priority order:

### Database Connection:
1. `ConnectionStrings__PostgresDb` environment variable
2. Individual `DB_*` environment variables
3. **AWS Secrets Manager** (`drone-configurator/postgres`)
4. `appsettings.json` (not recommended)

### JWT Secret:
1. `JWT_SECRET_KEY` environment variable
2. **AWS Secrets Manager** (`drone-configurator/jwt-secret`)
3. `appsettings.json` (not recommended)

## IAM Permissions Required

Your AWS user/role needs these permissions:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret"
      ],
      "Resource": [
        "arn:aws:secretsmanager:ap-south-1:975201825754:secret:drone-configurator/*"
      ]
    }
  ]
}
```

## Running the Application

### Development (with .env file):
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.API
dotnet run
```

### Production (with AWS Secrets Manager):
```powershell
# Set AWS region
$env:AWS_REGION="ap-south-1"

# Run application (will automatically use AWS Secrets Manager)
dotnet run --configuration Release
```

## Troubleshooting

### Error: "Unable to retrieve secret"
- Verify AWS credentials are configured
- Check IAM permissions
- Ensure secret exists: `aws secretsmanager describe-secret --secret-id drone-configurator/postgres`

### Error: "Access Denied"
- Add IAM permissions (see above)
- Verify you're using the correct AWS account

### Fallback to .env
- If AWS Secrets Manager fails, application will fall back to `.env` file
- Check console logs for which configuration source is being used
