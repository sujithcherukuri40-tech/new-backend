# ?? Pavaman Drone Configurator - Startup Guide

## ? Prerequisites Checklist

- [ ] .NET 9 SDK installed
- [ ] PostgreSQL database accessible (AWS RDS or local)
- [ ] Environment variables configured
- [ ] Visual Studio 2022 or VS Code with C# extension

---

## ?? Quick Start (Single Command)

### From UI Directory:
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
.\start-both.ps1
```

This will:
1. ? Build the solution
2. ? Apply database migrations
3. ? Create default admin user
4. ? Start API server (background)
5. ? Start UI application (foreground)
6. ? Auto-cleanup when you close the UI

### Options:
```powershell
# Skip build (faster startup after first build)
.\start-both.ps1 -SkipBuild

# Production mode
.\start-both.ps1 -Production
```

---

## ?? First-Time Login

### Option 1: Direct Login (DEBUG builds only)
1. Launch the app in DEBUG mode
2. Click **"?? Quick Login (Dev)"** on login screen
3. Auto-logged in as admin

### Option 2: Manual Login
1. Enter credentials:
   ```
   Email: admin@droneconfig.local
   Password: Admin@123
   ```
2. Click **"Sign In"**

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

## ??? Database Setup

### Automatic (Recommended)
The app automatically:
1. Applies migrations on startup
2. Creates tables if they don't exist
3. Seeds default admin user

### Manual Migration (If Needed)
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.API
dotnet ef database update
```

---

## ?? Configuration

### Backend (.env file)

Create `.env` in `PavamanDroneConfigurator.API/`:

```bash
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

### Frontend (appsettings.json)

Default config in `PavamanDroneConfigurator.UI/appsettings.json`:

```json
{
  "Auth": {
    "ApiUrl": "http://localhost:5000"
  },
  "ConnectionStrings": {
    "PostgresDb": "Host=localhost;Database=drone_local;Username=postgres;Password=postgres"
  }
}
```

---

## ?? Testing the System

### 1. Test Authentication
- [x] Login with admin credentials
- [x] Verify token storage
- [x] Test logout
- [x] Test session persistence (restart app)

### 2. Test Admin Panel
- [x] Navigate to "?? User Management"
- [x] Create new user (register)
- [x] Approve user from admin panel
- [x] Change user role
- [x] Disapprove user

### 3. Test User Registration
- [x] Click "Create one" on login
- [x] Fill registration form
- [x] Verify "Pending Approval" status
- [x] Login as admin and approve
- [x] Login as new user

### 4. Test Role-Based Access
- [x] Login as User ? Admin panel hidden
- [x] Login as Admin ? Admin panel visible
- [x] Verify API returns 403 for non-admin accessing `/admin/*`

---

## ?? Common Issues & Fixes

### Issue: "Unable to connect to server"
**Fix:**
```powershell
# Check if API is running
curl http://localhost:5000/health

# If not, start API
cd PavamanDroneConfigurator.API
dotnet run
```

### Issue: "Database connection failed"
**Fix:**
1. Verify `.env` file exists in API directory
2. Check database credentials
3. Test database connectivity:
   ```powershell
   psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com -U new_app_user -d drone_configurator
   ```

### Issue: "Admin panel not showing"
**Fix:**
1. Ensure you're logged in as admin
2. Check role in database:
   ```sql
   SELECT email, role, is_approved FROM users WHERE email = 'admin@droneconfig.local';
   ```
3. Role should be `Admin` (case-sensitive)

### Issue: "Account pending approval"
**Fix:**
1. Login as admin
2. Go to Admin Panel
3. Click "Approve" next to the user

### Issue: "Direct Login button not showing"
**Fix:**
- Only visible in DEBUG builds
- Use Release build for production

---

## ?? Security Checklist

### Development:
- [x] Default admin user created automatically
- [x] Tokens encrypted with DPAPI
- [x] JWT with 15-minute expiration
- [x] Refresh token rotation

### Production:
- [ ] Change default admin password
- [ ] Use AWS Secrets Manager for JWT secret
- [ ] Enable HTTPS
- [ ] Use environment variables (no hardcoded secrets)
- [ ] Set strong JWT secret (32+ characters)
- [ ] Configure CORS for production domain
- [ ] Remove DEBUG features

---

## ?? System Requirements

### Backend (API):
- .NET 9 Runtime
- PostgreSQL 14+
- 512 MB RAM minimum
- 1 GB disk space

### Frontend (UI):
- Windows 10/11 (x64)
- .NET 9 Desktop Runtime
- 512 MB RAM minimum
- 500 MB disk space

---

## ?? Update Workflow

### Pull Latest Changes:
```powershell
cd C:\Pavaman\config
git pull origin main
```

### Restore Dependencies:
```powershell
dotnet restore
```

### Apply Migrations:
```powershell
cd PavamanDroneConfigurator.API
dotnet ef database update
```

### Build & Run:
```powershell
cd ..\PavamanDroneConfigurator.UI
.\start-both.ps1
```

---

## ?? Logs & Debugging

### API Logs:
- Console output (when running `dotnet run`)
- Check for database connection errors
- JWT validation errors
- User authentication events

### UI Logs:
- Debug ? Output window in Visual Studio
- Console output when running from terminal
- Look for auth errors, network errors

### Database Logs:
```sql
-- View all users
SELECT * FROM users ORDER BY created_at DESC;

-- View refresh tokens
SELECT * FROM refresh_tokens WHERE revoked = false ORDER BY created_at DESC;

-- Check user approval status
SELECT email, is_approved, role FROM users;
```

---

## ?? Next Steps

1. ? Start both API and UI
2. ? Login with admin credentials
3. ? Test admin panel functionality
4. ? Create test user and approve
5. ? Test role-based access
6. ? Change default admin password
7. ? Configure for production

---

## ?? Support

For technical issues:
1. Check logs (API and UI consoles)
2. Verify database connectivity
3. Review `.env` configuration
4. Check [ADMIN_GUIDE.md](./ADMIN_GUIDE.md)
5. Review [README.md](./README.md)

---

**Ready to launch! ??**

**Version:** 1.0.0  
**Last Updated:** January 2025  
**© Pavaman Drone Configurator**
