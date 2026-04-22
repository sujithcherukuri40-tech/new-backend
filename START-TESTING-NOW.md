# ?? QUICK START - TESTING YOUR APP NOW

**Everything is ready!** Here's what you need to do:

---

## ? **STEP 1: START THE DESKTOP APP** (30 seconds)

```powershell
cd C:\Pavaman\kft-comfig\PavamanDroneConfigurator.UI
dotnet run
```

**Expected:** App window opens showing login screen

---

## ? **STEP 2: TEST LOGIN** (10 seconds)

**Login with admin credentials:**
- Email: `admin@kft.local`
- Password: `KftAdmin@2026!`

Click "Login"

**Expected:** Main window opens with all tabs visible

---

## ? **STEP 3: TEST YOUR FLOW** (Follow the guide)

Open **`UI-TESTING-GUIDE.md`** and test each flow:

### **Priority 1: Authentication**
1. ? Logout
2. ? Register new account (use your real email)
3. ? Check email for OTP from `noreply@kftgcs.com`
4. ? Enter OTP
5. ? Login as admin
6. ? Approve the new user
7. ? Test "Forgot Password" flow

### **Priority 2: Admin Features**
1. ? Admin Dashboard ? View users
2. ? User Approvals ? Approve/Reject
3. ? Role Management ? Change user roles

### **Priority 3: Firmware**
1. ? Firmware Management ? View firmware list
2. ? Download firmware from S3
3. ? Upload new firmware (admin)
4. ? Flash firmware (if drone connected)

### **Priority 4: Parameter Logs**
1. ? View parameter change logs
2. ? Search by user/drone
3. ? Download log files

### **Priority 5: Parameter Locking**
1. ? Create parameter lock
2. ? Test locked parameters cannot be changed
3. ? Edit/Delete locks

---

## ?? **QUICK DIAGNOSTICS**

### If app won't start:
```powershell
dotnet build
dotnet run --project PavamanDroneConfigurator.UI
```

### If login fails:
```powershell
# Test API from Windows
curl http://13.235.13.233:5000/health
```

### If email not received:
- Check spam folder
- Verify SES email in AWS Console
- Check API logs on EC2

---

## ?? **YOUR TESTING CHECKLIST**

```
[ ] Login works
[ ] Registration + Email OTP works
[ ] Forgot password + Email works
[ ] Firmware list loads from S3
[ ] Firmware download works
[ ] Admin dashboard shows users
[ ] User approval works
[ ] Firmware upload works (admin)
[ ] Parameter logs visible
[ ] Parameter locking works
[ ] Logout works
```

---

## ?? **NEED HELP?**

| Issue | Check This |
|-------|------------|
| Can't connect to API | `curl http://13.235.13.233:5000/health` |
| No firmwares showing | Check S3 bucket has files in `firmware/` folder |
| Email not received | Check SES email verified in AWS Console |
| Login fails | Verify API is running on EC2 |

---

## ?? **IMPORTANT FILES**

- **Testing Guide:** `UI-TESTING-GUIDE.md` (complete test cases)
- **Deployment Status:** `FINAL-DEPLOYMENT-STATUS.md` (what's wired)
- **API Testing:** `API-ENDPOINT-TEST-GUIDE.md` (API verification)
- **Production Audit:** `PRODUCTION-READINESS-AUDIT.md` (security checklist)

---

## ? **FINAL CHECKLIST**

Everything is ready:

- [x] ? API running on `http://13.235.13.233:5000`
- [x] ? Desktop app built successfully
- [x] ? All UI pages wired
- [x] ? All services configured
- [x] ? Email service ready
- [x] ? S3 bucket accessible
- [x] ? Database connected
- [x] ? Authentication working
- [x] ? Admin features ready
- [x] ? Documentation complete

---

**Just run the app and start testing!** ??

```powershell
dotnet run --project PavamanDroneConfigurator.UI
```

**Good luck with testing!** ??
