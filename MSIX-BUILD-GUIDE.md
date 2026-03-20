# Building MSIX Package for Microsoft Store

## ?? Quick Start

### Option 1: Using the Build Script (Recommended)
```powershell
.\Build-MSIXForStore.ps1
```

For Debug build:
```powershell
.\Build-MSIXForStore.ps1 -Configuration Debug
```

### Option 2: Using Visual Studio
1. Open `PavamanDroneConfigurator.sln`
2. Set configuration to **Release**
3. Set platform to **x64**
4. Right-click `PavamanDroneConfigurator.Package` project
5. Select **Store** ? **Create App Packages**
6. Follow the wizard

### Option 3: Using Command Line
```powershell
msbuild PavamanDroneConfigurator.Package\PavamanDroneConfigurator.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:AppxBundlePlatforms="x86|x64"
```

## ?? Output Location

After building, find your package at:
```
PavamanDroneConfigurator.Package\AppPackages\PavamanDroneConfigurator.Package_<version>_Test\
```

Files generated:
- **`*.msixupload`** - Upload this to Microsoft Store
- **`*.msixbundle`** - Contains both x64 and x86 packages
- **`*.cer`** - Certificate (not needed for Store)

## ?? Microsoft Store Submission Steps

### 1. Register Microsoft Partner Center Account
- Go to: https://partner.microsoft.com/dashboard
- Sign up for a Microsoft Developer account ($19 one-time fee for individuals)

### 2. Reserve Your App Name
- Navigate to **Apps and games** ? **New product**
- Reserve the name "Pavaman Configurator" (or your preferred name)

### 3. Create a New Submission
- Click on your app
- Select **Start your submission**

### 4. Update Package.appxmanifest (IMPORTANT!)
Before building for Store, you need to associate your package with the Store:

**In Visual Studio:**
1. Right-click `PavamanDroneConfigurator.Package` project
2. Select **Publish** ? **Associate App with the Store**
3. Sign in with your Partner Center account
4. Select your app name
5. This will update the Identity section in `Package.appxmanifest`

**The Identity section should look like:**
```xml
<Identity
    Name="YourPublisherID.PavamanConfigurator"
    Publisher="CN=YourPublisherName"
    Version="1.0.11.0" />
```

### 5. Build and Upload
1. Build the package using the script or Visual Studio
2. In Partner Center, go to **Packages**
3. Upload the `.msixupload` file
4. The system will automatically validate your package

### 6. Complete Store Listing
Fill in the required information:

**Properties:**
- Category: Business / Utilities & tools
- Privacy policy URL (required)
- Copyright and trademark info

**Age ratings:**
- Complete the questionnaire

**Pricing and availability:**
- Markets: Select where to distribute
- Pricing: Free or paid

**Store listings:**
- Description (10,000 character limit)
- Screenshots (at least 1, recommended 3-5)
  - 1366 x 768 or higher
  - PNG or JPG format
- App tile icon (optional)
- Promotional images (optional)

**Example Description:**
```
Pavaman Drone Configurator is a professional desktop application for configuring and managing UAV flight controllers. 

Features:
• Connect to flight controllers via serial communication
• Configure firmware settings
• Real-time telemetry monitoring
• Secure cloud integration
• Parameter management
• Flight data visualization

Perfect for drone enthusiasts, professionals, and developers who need a reliable tool for drone configuration and management.
```

### 7. Submit for Certification
- Review all sections
- Click **Submit to the Store**
- Certification typically takes 24-48 hours

## ?? Configuration Details

### Current Package Configuration
- **Package Name:** PavamanAviation.PavamanConfigurator
- **Display Name:** Pavaman Configurator
- **Publisher:** Pavaman Aviation
- **Version:** 1.0.11.0
- **Platforms:** x64, x86
- **Min Windows Version:** Windows 10 Fall Creators Update (10.0.17763.0)
- **Target Windows Version:** Windows 11 (10.0.26100.0)

### Capabilities Required
- Internet connectivity
- Private network access
- Removable storage
- Serial communication (USB devices)
- Full trust execution

## ?? Troubleshooting

### Build Errors

**Error: "The project needs to be associated with the Microsoft Store"**
- Solution: Right-click Package project ? Publish ? Associate App with the Store

**Error: "Certificate not found"**
- For Store: This is OK, Store handles signing
- For local testing: You need a test certificate

**Error: "Platform mismatch"**
- Ensure UI project has `<Platforms>AnyCPU;x64;x86</Platforms>`
- Clean and rebuild solution

### Package Validation Errors

**"Package acceptance validation error: Apps converted with the Desktop App Converter must set the TrustLevel to mediumIL"**
- Already configured in your Package.appxmanifest ?

**"The package must be signed"**
- For Store submission: Ignore this, Store signs it
- For local testing: Use `Add-AppxPackage -Path "package.msix" -DeferRegistration`

## ?? Before Store Submission Checklist

- [ ] App name reserved in Partner Center
- [ ] Package associated with Store (Identity updated)
- [ ] Version number incremented if updating
- [ ] All screenshots prepared (PNG/JPG, 1366x768+)
- [ ] Privacy policy URL ready
- [ ] App description written (under 10,000 chars)
- [ ] Age rating questionnaire completed
- [ ] Markets and pricing configured
- [ ] Package built successfully (Release configuration)
- [ ] Package tested locally

## ?? Testing Locally Before Store Upload

To test your MSIX package locally:

1. **Enable Developer Mode:**
   - Settings ? Privacy & security ? For developers ? Developer Mode

2. **Install the package:**
   ```powershell
   Add-AppxPackage -Path ".\PavamanDroneConfigurator.Package\AppPackages\...\*.msixbundle"
   ```

3. **Launch from Start Menu:**
   - Search for "Pavaman Configurator"

4. **Uninstall for testing:**
   ```powershell
   Get-AppxPackage *PavamanConfigurator* | Remove-AppxPackage
   ```

## ?? Additional Resources

- [Microsoft Store Submission Guide](https://learn.microsoft.com/windows/apps/publish/)
- [MSIX Packaging Documentation](https://learn.microsoft.com/windows/msix/)
- [Partner Center Dashboard](https://partner.microsoft.com/dashboard)
- [App Certification Requirements](https://learn.microsoft.com/windows/apps/publish/store-policies)

## ?? Need Help?

If you encounter issues:
1. Check the build output for specific errors
2. Review the Partner Center certification report
3. Ensure all capabilities are properly declared
4. Verify minimum Windows version requirements

---

**Current Version:** 1.0.11.0  
**Last Updated:** 2024  
**Target Framework:** .NET 9 (Windows)
