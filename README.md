# Pavaman Drone Configurator

A Windows-only Avalonia-based drone configurator application with Clean Architecture layout.

## Quick Start Guide

### ?? How to Run (Single Command)

#### From UI Directory:
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
.\start-both.ps1
```

This will:
1. ? Build the solution
2. ? Apply database migrations
3. ? Start API server (background)
4. ? Start UI application (foreground)
5. ? Auto-cleanup when you close the UI

#### Options:
```powershell
# Skip build (faster startup after first build)
.\start-both.ps1 -SkipBuild

# Production mode
.\start-both.ps1 -Production
```

---

## ?? Project Structure

```
C:\Pavaman\config\
??? PavamanDroneConfigurator.API\          # Backend API Server
?   ??? .env                               # Local secrets (gitignored)
?   ??? .env.example                       # Template
?   ??? appsettings.json                   # Configuration
?   ??? Program.cs                         # API entry point
?
??? PavamanDroneConfigurator.UI\           # Desktop Application
?   ??? start-both.ps1                     # ?? START HERE!
?   ??? appsettings.json                   # UI configuration
?   ??? App.axaml.cs                       # UI entry point
?   ??? Views\Auth\                        # Login/Register views
?   ??? ViewModels\Auth\                   # MVVM ViewModels
?
??? PavamanDroneConfigurator.Core\         # Domain models
?   ??? Models\Auth\                       # Auth data models
?
??? PavamanDroneConfigurator.Infrastructure\ # Services
    ??? Services\Auth\                     # Auth business logic
```

---

## ?? Configuration

### Environment Variables (.env file)

The API uses `.env` file for local development:

**Location:** `PavamanDroneConfigurator.API\.env`

```sh
# Database
DB_HOST=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=new_app_user
DB_PASSWORD=Sujith2007
DB_SSL_MODE=Require

# JWT
JWT_SECRET_KEY=kZx9mP2qR7tY4wV8nB3cF6hJ1lN5oS0uA9dG2kM5pQ8rT7vW4xE1yH6jL3nP0sU
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient

# AWS (optional)
AWS_REGION=ap-south-1
AWS_SECRETS_MANAGER_DB_SECRET=drone-configurator/postgres
AWS_SECRETS_MANAGER_JWT_SECRET=drone-configurator/jwt-secret
```

### Configuration Priority

1. **Environment variables** (`.env` file or system)
2. **AWS Secrets Manager** (production)
3. **appsettings.json** (fallback)

---

## ?? Documentation

- **[PRODUCTION_DEPLOYMENT.md](./PRODUCTION_DEPLOYMENT.md)** - Production deployment guide
- **[AWS_SECRETS_MANAGER_SETUP.md](./AWS_SECRETS_MANAGER_SETUP.md)** - AWS Secrets Manager integration

---

## ??? Manual Startup (If Script Fails)

### Step 1: Start API
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.API
dotnet run
```
**Keep this terminal open!**

### Step 2: Start UI (New Terminal)
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```

---

## ? First Time Setup

1. **Clone repository**
   ```powershell
   git clone https://github.com/sujithcherukuri40-tech/drone-config
   cd drone-config
   ```

2. **Create .env file**
   ```powershell
   cd PavamanDroneConfigurator.API
   Copy-Item .env.example .env
   # Edit .env with your credentials
   ```

3. **Run application**
   ```powershell
   cd ..\PavamanDroneConfigurator.UI
   .\start-both.ps1
   ```

---

## ?? Troubleshooting

### "Unable to connect to server"
- ? Ensure API is running (check terminal output)
- ? Verify API URL: `http://localhost:5000/health`
- ? Check `.env` file exists in API directory

### Database migration errors
- ? Verify database credentials in `.env`
- ? Check network access to AWS RDS
- ? Run manually: `dotnet ef database update`

### Build errors
- ? Restore packages: `dotnet restore`
- ? Clean build: `dotnet clean && dotnet build`

---

## ?? Architecture (MVVM)

This project follows **Clean Architecture** with **MVVM pattern**:

| Layer | Purpose | Location |
|-------|---------|----------|
| **Presentation** | UI, Views, ViewModels | `UI/` |
| **Domain** | Business models | `Core/Models/` |
| **Application** | Interfaces, DTOs | `Core/Interfaces/` |
| **Infrastructure** | Services, Data Access | `Infrastructure/` |
| **API** | REST endpoints | `API/` |

### MVVM Components

- **Model**: `Core/Models/Auth/` (AuthState, UserInfo)
- **View**: `UI/Views/Auth/` (LoginView.axaml)
- **ViewModel**: `UI/ViewModels/Auth/` (LoginViewModel.cs)
- **Service**: `Infrastructure/Services/Auth/` (AuthApiService.cs)

---

## ?? Key Features

? **Authentication**
- User registration (pending approval)
- Login with JWT tokens
- Token refresh mechanism
- Secure token storage

? **Security**
- BCrypt password hashing
- JWT-based authentication
- Environment variable configuration
- AWS Secrets Manager support

? **Database**
- PostgreSQL on AWS RDS
- Entity Framework Core migrations
- User and token management

---

## ?? Support

For issues, check:
1. Terminal output for error messages
2. API logs in first terminal
3. UI logs in second terminal
4. Database connectivity

---

**Ready to fly! ??**
