# Production Deployment Guide

## Overview

This guide covers the complete authentication implementation for the Pavaman Drone Configurator, now production-ready with secure database integration and JWT authentication.

## Changes Summary

### ? Completed Changes

1. **Environment Configuration**
   - ? Created `.env` file with production database credentials
   - ? Updated `appsettings.json` for both API and UI projects
   - ? Configured secure JWT secret (64-character random key)
   - ? Set up production database connection string

2. **Security Enhancements**
   - ? Removed "Skip Login" development mode functionality
   - ? Enforced JWT secret validation (minimum 32 characters)
   - ? Implemented database migrations instead of EnsureCreated
   - ? Added production-ready error handling

3. **Authentication Flow Fixes**
   - ? Fixed window transition crash using Dispatcher.UIThread
   - ? Removed dev mode shortcuts from LoginViewModel and AuthShellViewModel
   - ? Updated LoginView.axaml to remove Skip Login button
   - ? Implemented proper async initialization for AuthShell

4. **Database Configuration**
   - ? Created database migration: `InitialAuthMigration`
   - ? Configured PostgreSQL connection with SSL (Require mode)
   - ? Set up proper connection string fallback hierarchy

## Database Schema

### Tables Created

#### `users`
- `id` (uuid, primary key)
- `full_name` (varchar(100), required)
- `email` (varchar(256), unique, required)
- `password_hash` (varchar(256), required)
- `is_approved` (boolean, default: false)
- `role` (varchar(20), default: 'User')
- `created_at` (timestamp, default: CURRENT_TIMESTAMP)
- `last_login_at` (timestamp, nullable)

#### `refresh_tokens`
- `id` (uuid, primary key)
- `user_id` (uuid, foreign key to users)
- `token` (varchar(512), unique, required)
- `expires_at` (timestamp, required)
- `revoked` (boolean, default: false)
- `created_at` (timestamp, default: CURRENT_TIMESTAMP)
- `created_by_ip` (varchar(45), nullable)
- `revoked_at` (timestamp, nullable)
- `revoked_reason` (varchar(256), nullable)

## Deployment Steps

### Prerequisites

1. **.NET 9.0 SDK** installed
2. **PostgreSQL Database** (AWS RDS configured)
3. **Network access** to database endpoint
4. **SSL certificate** for HTTPS (optional but recommended)

### Step 1: Apply Database Migrations

```bash
# Navigate to API project directory
cd C:\Pavaman\config\PavamanDroneConfigurator.API

# Apply migrations to production database
dotnet ef database update

# Verify migration was applied
dotnet ef migrations list
```

Expected output:
```
? Loaded environment variables from .env file
Applying migration '20260128XXXXXX_InitialAuthMigration'.
Done.
```

### Step 2: Create Initial Admin User

Connect to the database and create the first admin user:

```sql
-- Connect to database
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"

-- Create admin user (password will be hashed by the application)
-- You'll need to register through the app first, then update this user to admin
```

**Important:** The first user should register through the application, then be manually approved:

```sql
-- After registration, approve the first user and set as admin
UPDATE users 
SET is_approved = true, role = 'Admin' 
WHERE email = 'admin@example.com';
```

### Step 3: Start the API

```bash
cd C:\Pavaman\config\PavamanDroneConfigurator.API

# Run in production mode
dotnet run --configuration Release
```

Expected startup logs:
```
? Loaded environment variables from .env file
Database migrations applied successfully
Database: drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
JWT Issuer: DroneConfigurator
Environment: Production
Drone Configurator Auth API starting...
```

### Step 4: Start the UI Application

```bash
cd C:\Pavaman\config\PavamanDroneConfigurator.UI

# Build and run
dotnet run --configuration Release
```

Expected startup:
```
? Loaded environment variables from .env file
?? Auth API URL: http://localhost:5000
Application started successfully
```

## Configuration Details

### Environment Variables Priority

The application uses this priority order for configuration:

1. **Environment Variables** (highest priority)
2. **appsettings.json**
3. **Default values** (lowest priority)

### Database Connection String

The connection string is built using this priority:

1. `ConnectionStrings__PostgresDb` environment variable
2. Individual variables (`DB_HOST`, `DB_NAME`, `DB_USER`, `DB_PASSWORD`, `DB_SSL_MODE`)
3. `ConnectionStrings:PostgresDb` from appsettings.json

Current production configuration:
```
Host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
Port=5432
Database=drone_configurator
Username=new_app_user
Password=Sujith2007
Ssl Mode=Require
```

### JWT Configuration

- **Secret Key**: 64-character random string (stored in .env)
- **Issuer**: DroneConfigurator
- **Audience**: DroneConfiguratorClient
- **Access Token Lifetime**: 15 minutes
- **Refresh Token Lifetime**: 7 days

## Authentication Flow

### 1. Registration
```
User enters credentials ? API validates ? Creates user (is_approved=false) 
? Returns user info (no tokens) ? Shows "Pending Approval" screen
```

### 2. Admin Approval
```
Admin connects to database ? Updates is_approved=true 
? User can now login
```

### 3. Login
```
User enters credentials ? API validates ? Checks is_approved 
? If approved: generates JWT + refresh token ? Returns tokens 
? UI stores tokens ? Shows main application
```

### 4. Token Refresh
```
Access token expires ? UI detects expiry ? Sends refresh token 
? API validates ? Generates new tokens ? UI updates storage
```

## Security Features

### Implemented

? **Password Hashing**: BCrypt with automatic salting  
? **JWT Authentication**: HS256 signing algorithm  
? **Token Rotation**: Refresh tokens are single-use  
? **Approval Workflow**: Admin must approve new users  
? **Secure Storage**: Tokens encrypted at rest  
? **Connection String Security**: No hardcoded credentials  
? **SSL Required**: Database connections use SSL  
? **Input Validation**: All user inputs validated  
? **Error Handling**: Production-ready error messages  

### Password Requirements

- Minimum 8 characters
- At least 1 uppercase letter
- At least 1 lowercase letter
- At least 1 digit

## API Endpoints

### Public Endpoints

- `POST /auth/register` - Register new user
- `POST /auth/login` - Authenticate user
- `POST /auth/refresh` - Refresh access token

### Protected Endpoints

- `GET /auth/me` - Get current user info (requires JWT)
- `POST /auth/logout` - Logout user (requires JWT)

### Health Check

- `GET /health` - API health status

## Troubleshooting

### Issue: "Database connection failed"

**Solution:**
1. Verify `.env` file exists in API project root
2. Check database credentials are correct
3. Ensure network access to RDS endpoint
4. Verify SSL mode is set to "Require"

```bash
# Test connection
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"
```

### Issue: "JWT secret key not configured"

**Solution:**
1. Check `.env` file has `JWT_SECRET_KEY`
2. Ensure key is at least 32 characters
3. Verify environment variables are loaded

```bash
# View loaded environment variables
dotnet run -- --environment Production
```

### Issue: "Unable to connect to auth API"

**Solution:**
1. Ensure API is running on http://localhost:5000
2. Check UI appsettings.json has correct Auth:ApiUrl
3. Verify firewall allows local connections

### Issue: "Account pending approval"

**Expected behavior**: New users must be approved by admin.

**Solution:**
```sql
-- Approve user in database
UPDATE users SET is_approved = true WHERE email = 'user@example.com';
```

### Issue: "Window transition crash"

**Fixed**: This was caused by cross-thread UI updates. The fix uses `Dispatcher.UIThread.Post()` to ensure UI updates happen on the correct thread.

## Monitoring and Logs

### API Logs

Located in console output. Key events logged:
- Database migrations applied
- User registrations
- Login attempts (success/failure)
- Token refreshes
- Logout events

### UI Logs

Located in console output. Key events logged:
- Environment variables loaded
- Auth API URL configured
- Authentication state changes
- Token storage operations

## Backup and Recovery

### Database Backup

Use AWS RDS automated backups or manual snapshots:

```bash
# Using AWS CLI (if configured)
aws rds create-db-snapshot \
  --db-instance-identifier drone-configurator-db \
  --db-snapshot-identifier drone-config-backup-$(date +%Y%m%d)
```

### User Data Export

```sql
-- Export all users
COPY (SELECT id, full_name, email, is_approved, role, created_at, last_login_at FROM users) 
TO '/tmp/users_export.csv' WITH CSV HEADER;
```

## Production Checklist

Before going live:

- [ ] Database migrations applied successfully
- [ ] At least one admin user created and approved
- [ ] API running and accessible
- [ ] UI can connect to API
- [ ] Registration flow tested
- [ ] Login flow tested
- [ ] Token refresh tested
- [ ] Logout tested
- [ ] Approval workflow tested
- [ ] Error handling tested
- [ ] SSL certificates configured (if using HTTPS)
- [ ] Firewall rules configured
- [ ] Backup strategy in place
- [ ] Monitoring configured

## Next Steps

1. **Create More Admin Users**: Approve and promote users to admin role
2. **Configure HTTPS**: Set up SSL certificates for production
3. **Set up Monitoring**: Configure application insights or logging
4. **User Training**: Create documentation for end users
5. **Disaster Recovery Plan**: Document recovery procedures

## Support

For issues or questions:
1. Check this deployment guide
2. Review application logs
3. Check database connection
4. Verify environment variables

## Version Information

- **.NET Version**: 9.0
- **Entity Framework Core**: 9.0.0
- **PostgreSQL**: 10.0+
- **BCrypt**: 4.0.3
- **JWT Bearer**: 9.0.0
