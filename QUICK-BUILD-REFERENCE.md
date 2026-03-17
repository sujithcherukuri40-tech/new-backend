# ?? Quick Build Commands

## Build for Microsoft Store
```powershell
# Using MSBuild (RECOMMENDED)
msbuild PavamanDroneConfigurator.Package\PavamanDroneConfigurator.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Always /p:AppxBundlePlatforms="x86|x64" /p:UapAppxPackageBuildMode=StoreUpload /verbosity:minimal

# Or using the PowerShell script
.\Build-MSIXForStore.ps1
```

## Find Your Package
```powershell
cd PavamanDroneConfigurator.Package\AppPackages
dir *.msixupload
```

**Package Location:**
```
PavamanDroneConfigurator.Package\AppPackages\PavamanDroneConfigurator.Package_1.0.11.0_x86_x64_bundle.msixupload
```

## Test Installation Locally
```powershell
# Enable Developer Mode first in Windows Settings!
Add-AppxPackage -Path "PavamanDroneConfigurator.Package\AppPackages\PavamanDroneConfigurator.Package_1.0.11.0_Test\PavamanDroneConfigurator.Package_1.0.11.0_x86_x64.msixbundle"
```

## Uninstall Test Package
```powershell
Get-AppxPackage *PavamanConfigurator* | Remove-AppxPackage
```

## Important Links
- **Partner Center:** https://partner.microsoft.com/dashboard
- **App Packages Location:** `PavamanDroneConfigurator.Package\AppPackages\`
- **Upload File:** `PavamanDroneConfigurator.Package_1.0.11.0_x86_x64_bundle.msixupload`

## Before First Build for Store
1. Go to Partner Center and reserve your app name
2. In Visual Studio: Right-click Package project ? Publish ? Associate App with the Store
3. Build using the command or script above
4. Upload the `.msixupload` file to Partner Center

## Current Configuration
- **Platforms:** x64, x86 (bundled together)
- **Version:** 1.0.11.0
- **Signing:** Handled by Microsoft Store (no certificate needed!)
- **Package Type:** MSIX Bundle (.msixbundle)
- **Package Size:** ~113 MB

## Expected Build Warnings (Safe to Ignore)
- `MVVMTK0034`: ObservableProperty warnings - cosmetic only, doesn't affect functionality
- `NETSDK1047`: Dependency resolution for Core/Infrastructure - these are resolved at runtime
- `NU1608`: HarfBuzzSharp version constraint - Avalonia handles this compatibility

## Increment Version
Edit `PavamanDroneConfigurator.Package\Package.appxmanifest`:
```xml
<Identity Version="1.0.12.0" />
```
Note: Last number (.0) is auto-incremented if AppxAutoIncrementPackageRevision is True

---
**Need more details?** See `MSIX-BUILD-GUIDE.md`

**? BUILD VERIFIED:** Package builds successfully and is ready for Microsoft Store submission!
