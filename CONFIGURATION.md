# ?? Configuration Guide

## Application Configuration

This application requires configuration files that contain sensitive information. **Never commit credentials to Git!**

### Configuration Files

Create these files based on the templates provided:

#### 1. Backend API Configuration

**File**: `PavamanDroneConfigurator.API/appsettings.json`

```json
{
  "ConnectionStrings": {
    "PostgresDb": "Host=YOUR_DB_HOST;Port=5432;Database=drone_configurator;Username=YOUR_USERNAME;Password=YOUR_PASSWORD;Ssl Mode=Prefer"
  },
  "Jwt": {
    "Key": "YOUR_SECRET_KEY_MIN_32_CHARACTERS_LONG",
    "Issuer": "PavamanDroneConfigurator",
    "Audience": "PavamanDroneApp"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5000",
      "https://localhost:5001"
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

#### 2. Frontend UI Configuration

**File**: `PavamanDroneConfigurator.UI/appsettings.json`

```json
{
  "ConnectionStrings": {
    "PostgresDb": "Host=YOUR_DB_HOST;Port=5432;Database=drone_configurator;Username=YOUR_USERNAME;Password=YOUR_PASSWORD;Ssl Mode=Prefer"
  },
  "Auth": {
    "ApiUrl": "http://localhost:5000",
    "TokenExpiryBufferSeconds": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

---

## Environment-Specific Configuration

### Local Development

Use `localhost` or `127.0.0.1` for database connection:

```json
{
  "ConnectionStrings": {
    "PostgresDb": "Host=localhost;Port=5432;Database=drone_configurator;Username=postgres;Password=your_local_password"
  }
}
```

### Production / Cloud Deployment

**IMPORTANT**: Never store credentials in configuration files for production!

Use one of these secure methods:

#### Option 1: Environment Variables

```json
{
  "ConnectionStrings": {
    "PostgresDb": "${DB_CONNECTION_STRING}"
  },
  "Jwt": {
    "Key": "${JWT_SECRET_KEY}"
  }
}
```

Then set environment variables:
```bash
export DB_CONNECTION_STRING="Host=your-server;Port=5432;..."
export JWT_SECRET_KEY="your-secret-key"
```

#### Option 2: AWS Secrets Manager (Recommended for AWS)

Install package:
```bash
dotnet add package Amazon.Extensions.Configuration.SystemsManager
```

Configure in Program.cs:
```csharp
builder.Configuration.AddSecretsManager();
```

#### Option 3: Azure Key Vault (For Azure deployments)

Install package:
```bash
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
```

#### Option 4: User Secrets (Development Only)

```bash
cd PavamanDroneConfigurator.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:PostgresDb" "Host=localhost;..."
dotnet user-secrets set "Jwt:Key" "your-secret-key"
```

---

## Database Setup

### PostgreSQL Installation

#### Windows:
Download from: https://www.postgresql.org/download/windows/

#### Linux:
```bash
sudo apt-get install postgresql postgresql-contrib
```

#### macOS:
```bash
brew install postgresql
```

### Create Database

```sql
CREATE DATABASE drone_configurator;
CREATE USER drone_app WITH PASSWORD 'your_secure_password';
GRANT ALL PRIVILEGES ON DATABASE drone_configurator TO drone_app;
```

### Run Migrations

```bash
cd PavamanDroneConfigurator.API
dotnet ef database update
```

---

## JWT Secret Key Generation

Generate a secure random key:

### PowerShell:
```powershell
$bytes = New-Object Byte[] 64
[Security.Cryptography.RNGCryptoServiceProvider]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

### Linux/macOS:
```bash
openssl rand -base64 64
```

---

## Security Checklist

- [ ] Never commit `appsettings.json` with real credentials
- [ ] Use `.gitignore` to exclude sensitive files
- [ ] Use environment variables or secrets management in production
- [ ] Generate strong JWT secret key (min 32 characters)
- [ ] Use SSL/TLS for database connections in production
- [ ] Rotate secrets regularly
- [ ] Use different credentials for dev/staging/production
- [ ] Enable database connection encryption
- [ ] Implement proper access control on database

---

## Troubleshooting

### "Unable to connect to database"
- Check PostgreSQL is running
- Verify connection string is correct
- Check firewall allows connection on port 5432
- Verify username/password are correct

### "JWT token validation failed"
- Ensure `Jwt:Key` is the same on API and UI
- Check `Jwt:Issuer` and `Jwt:Audience` match
- Verify key is at least 32 characters

### "Migration failed"
- Ensure database exists
- Check user has CREATE TABLE permission
- Verify connection string is correct

---

## Example: Complete Setup

1. **Install PostgreSQL**
2. **Create database and user**
3. **Copy appsettings.json.template to appsettings.json**
4. **Update credentials in appsettings.json**
5. **Add appsettings.json to .gitignore**
6. **Run migrations**
7. **Test application**

---

**Remember**: Configuration files with credentials should NEVER be committed to version control!
