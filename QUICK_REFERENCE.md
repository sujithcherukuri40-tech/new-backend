# ?? Quick Reference Card

## ?? **Start Application**

```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
.\START.bat
```

---

## ?? **Important Files**

| File | Purpose |
|------|---------|
| `README.md` | Quick start guide |
| `AUTHENTICATION_COMPLETE.md` | Full documentation |
| `PRODUCTION_DEPLOYMENT.md` | Deployment guide |
| `AWS_SECRETS_MANAGER_SETUP.md` | AWS integration |

---

## ?? **Configuration**

### API (.env in API folder)
```sh
DB_HOST=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
DB_PASSWORD=Sujith2007
JWT_SECRET_KEY=kZx9mP2qR7tY4wV8nB3cF6hJ1lN5oS0uA9dG2kM5pQ8rT7vW4xE1yH6jL3nP0sU
```

### UI (.env in UI folder)
```sh
AUTH_API_URL=http://localhost:5000
```

---

## ?? **Common Commands**

```powershell
# Start app (both API + UI)
cd PavamanDroneConfigurator.UI
.\start-both.ps1

# Apply database migrations
cd PavamanDroneConfigurator.API
dotnet ef database update

# Build solution
dotnet build

# Clean build
dotnet clean && dotnet build
```

---

## ?? **Approve User (Database)**

```sql
UPDATE users SET is_approved = true 
WHERE email = 'user@example.com';
```

---

## ?? **API Endpoints**

```
POST http://localhost:5000/auth/register
POST http://localhost:5000/auth/login
GET  http://localhost:5000/auth/me
POST http://localhost:5000/auth/refresh
POST http://localhost:5000/auth/logout
GET  http://localhost:5000/health
```

---

## ??? **Project Structure**

```
API/                    ? Backend server
UI/                     ? Desktop app (START HERE!)
Core/                   ? Domain models
Infrastructure/         ? Services
```

---

## ? **Quick Health Check**

1. ? API running? ? `http://localhost:5000/health`
2. ? UI running? ? Window should open
3. ? Database? ? Login should work for approved users

---

**Questions? Check `AUTHENTICATION_COMPLETE.md` for full details.**
