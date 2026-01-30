# Admin Panel & Authentication Guide

## ?? Default Admin Credentials

The system automatically creates a default admin user on first run:

```
Email: admin@droneconfig.local
Password: Admin@123
```

**?? SECURITY WARNING:** Change this password immediately after first login!

---

## ?? Quick Start (Development)

### Option 1: Direct Login Button (DEBUG builds only)

1. Start the application in DEBUG mode
2. On the login screen, click **"?? Quick Login (Dev)"**
3. You'll be automatically logged in as admin

### Option 2: Manual Login

1. Enter credentials:
   - Email: `admin@droneconfig.local`
   - Password: `Admin@123`
2. Click **"Sign In"**

---

## ?? Admin Panel Features

### User Management

Accessible via **"?? User Management"** in the sidebar (admin only).

#### Features:
- ? **Approve/Disapprove Users**: Control who can access the system
- ?? **Change User Roles**: Promote users to Admin or demote to User
- ?? **View User List**: See all registered users, their status, and roles
- ?? **Registration Date**: Track when users registered

#### Actions:
1. **Approve User**: Click green "Approve" button next to pending user
2. **Disapprove User**: Click orange "Disapprove" to revoke access
3. **Change Role**: Click "Change Role" to toggle between User ? Admin

---

## ?? Role-Based Access Control

### User Role
- ? Access all drone configuration features
- ? Cannot access Admin Panel
- ? Cannot approve other users

### Admin Role
- ? All User permissions
- ? Access Admin Panel
- ? Approve/disapprove users
- ? Change user roles

---

## ?? User Registration Flow

### For New Users:
1. Click **"Create one"** on login screen
2. Fill in registration form:
   - Full Name
   - Email
   - Password (min 8 chars, must include uppercase, lowercase, and number)
   - Confirm Password
3. Click **"Create Account"**
4. **Status**: Account created but **PENDING APPROVAL**
5. User cannot login until approved by admin

### For Admins:
1. New user appears in Admin Panel with **"Pending"** status
2. Click **"Approve"** to grant access
3. User can now login and use the application

---

## ?? API Endpoints (Backend)

### Admin Endpoints (Require Admin Role)

```
GET  /admin/users          - Get all users
POST /admin/users/{id}/approve - Approve/disapprove user
POST /admin/users/{id}/role    - Change user role
```

### Auth Endpoints (Public/Authenticated)

```
POST /auth/register  - Register new user
POST /auth/login     - Login with credentials
POST /auth/logout    - Logout current user
POST /auth/refresh   - Refresh access token
GET  /auth/me        - Get current user info
GET  /health         - Health check
```

---

## ??? Security Features

### Backend Security:
- ? JWT-based authentication
- ? BCrypt password hashing
- ? Refresh token rotation
- ? Role-based authorization (`[Authorize(Roles = "Admin")]`)
- ? Token expiration (15 minutes access token, 7 days refresh token)
- ? CORS configured for desktop app

### Frontend Security:
- ? DPAPI-encrypted token storage (Windows Data Protection)
- ? Automatic token refresh
- ? Secure HTTP-only communication
- ? Role-based UI hiding (admin features invisible to non-admins)
- ? Session persistence (login survives app restart)

---

## ?? Troubleshooting

### "Unable to connect to server"
- Ensure API is running at `http://localhost:5000`
- Check `.env` file in `PavamanDroneConfigurator.API/`
- Verify database connection

### "Account pending approval"
- User must be approved by admin
- Login to admin account and approve the user

### "Direct Login button not showing"
- Only visible in DEBUG builds
- Use Release build for production (button hidden)

### "Admin Panel not visible"
- Only visible to users with Admin role
- Regular users cannot see the admin menu

---

## ?? Production Deployment

### Remove Debug Features:
1. Build in **Release** configuration
2. Direct Login button automatically hidden
3. All debug logging reduced

### Security Checklist:
- [ ] Change default admin password
- [ ] Use environment variables for JWT secret
- [ ] Enable HTTPS in production
- [ ] Use AWS Secrets Manager for production secrets
- [ ] Set strong JWT secret (32+ characters)
- [ ] Configure proper CORS for production domain

---

## ?? Environment Variables

### Backend (API)

Required in `.env` file or environment:

```bash
# Database
DB_HOST=your-database-host
DB_NAME=drone_configurator
DB_USER=your-db-user
DB_PASSWORD=your-db-password
DB_SSL_MODE=Require

# JWT (CRITICAL - Use strong random key!)
JWT_SECRET_KEY=your-secure-random-secret-key-minimum-32-chars
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient

# AWS (Production)
AWS_REGION=ap-south-1
AWS_SECRETS_MANAGER_DB_SECRET=drone-configurator/postgres
AWS_SECRETS_MANAGER_JWT_SECRET=drone-configurator/jwt-secret
```

### Frontend (UI)

Optional in `appsettings.json`:

```json
{
  "Auth": {
    "ApiUrl": "http://localhost:5000"
  }
}
```

Or environment variable:
```bash
AUTH_API_URL=http://localhost:5000
```

---

## ?? Support

For issues or questions:
1. Check logs in API console
2. Check UI console (Debug ? Output)
3. Verify database connectivity
4. Review this documentation

---

**Version:** 1.0.0  
**Last Updated:** January 2025  
**© Pavaman Drone Configurator**
