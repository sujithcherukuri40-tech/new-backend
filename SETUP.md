# ?? Quick Setup Guide

## ? Fast Start

### 1. Run the Application

```powershell
cd PavamanDroneConfigurator.UI
.\start-both.ps1
```

This starts both API and desktop app automatically.

---

## ?? Manual Setup (If Needed)

### Desktop App Only

```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

### With Backend API

#### Step 1: Configure API

Create `PavamanDroneConfigurator.API/appsettings.Development.LOCAL.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=YOUR_RDS_HOST;Port=5432;Database=drone_configurator;Username=YOUR_USER;Password=YOUR_PASSWORD"
  },
  "AWS": {
    "AccessKeyId": "YOUR_AWS_KEY",
    "SecretAccessKey": "YOUR_AWS_SECRET",
    "Region": "ap-south-1",
    "BucketName": "pavaman-firmware-bucket"
  },
  "Jwt": {
    "SecretKey": "YOUR_JWT_SECRET_MINIMUM_32_CHARS",
    "Issuer": "PavamanDroneConfigurator",
    "Audience": "PavamanUsers",
    "ExpirationMinutes": 60
  }
}
```

?? **This file is in .gitignore - never commit it!**

#### Step 2: Run API

```powershell
cd PavamanDroneConfigurator.API
dotnet run
```

#### Step 3: Run Desktop App

```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

---

## ?? Security

- ? All credentials go in `.LOCAL.json` files
- ? These files are protected by `.gitignore`
- ? Desktop app connects to API, not AWS directly
- ? Safe to distribute desktop app to users

---

## ?? Configuration

### Desktop App

Edit `PavamanDroneConfigurator.UI/appsettings.json`:

```json
{
  "Api": {
    "BaseUrl": "http://localhost:5000"
  }
}
```

Change to your EC2 IP for production:
```json
{
  "Api": {
    "BaseUrl": "http://YOUR_EC2_IP:5000"
  }
}
```

---

## ? That's It!

Your app is now configured securely. All credentials stay local and are never committed to Git.
