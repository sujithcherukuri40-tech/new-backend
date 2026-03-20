# ?? Microsoft Store Submission Checklist

## ? Before Building

- [ ] **Version number updated** in `Package.appxmanifest` (if this is an update)
- [ ] **App tested locally** and working correctly
- [ ] **All dependencies** are properly included
- [ ] **Assets folder** contains all required files (appsettings.json, ParameterMetadata.xml)

## ? Partner Center Setup

- [ ] **Microsoft Developer account created** ($19 one-time fee)
- [ ] **App name reserved** in Partner Center
  - Suggested: "Pavaman Configurator" or "Pavaman Drone Configurator"
- [ ] **Package associated with Store**
  - In Visual Studio: Right-click Package project ? Publish ? Associate App with the Store

## ? Required Assets for Store Listing

### Screenshots (REQUIRED - at least 1, recommend 3-5)
- [ ] Resolution: 1366 x 768 or higher
- [ ] Format: PNG or JPG
- [ ] Show key features:
  - [ ] Main dashboard/interface
  - [ ] Flight controller connection screen
  - [ ] Parameter configuration
  - [ ] Telemetry/data visualization
  - [ ] Settings/configuration panel

### App Icons (check current status)
- [ ] Verify all icon sizes are present in `PavamanDroneConfigurator.Package\Images\`
- [ ] Icons should use your logo/branding
- Required sizes are already defined in Package.appxmanifest ?

### Privacy Policy (REQUIRED)
- [ ] **Privacy policy URL prepared**
- Must cover:
  - [ ] What data is collected (telemetry, flight data, user settings)
  - [ ] How data is used
  - [ ] How data is stored
  - [ ] User rights (access, deletion, etc.)
  - [ ] Contact information

Example URL: `https://pavamanaviation.com/privacy-policy`

## ? Store Listing Content

### App Description (REQUIRED)
- [ ] **Short description** (100-200 characters)
  ```
  Professional drone configuration tool for UAV flight controllers with real-time telemetry and cloud integration.
  ```

- [ ] **Full description** (up to 10,000 characters)
  - [ ] What the app does
  - [ ] Key features (bullet points)
  - [ ] System requirements
  - [ ] Target users
  - [ ] Support/contact information

### Additional Info
- [ ] **Category selected**
  - Suggested: Business > Utilities & tools
- [ ] **Age rating completed**
  - PEGI/ESRB questionnaire
- [ ] **Copyright info** (e.g., "© 2024 Pavaman Aviation")
- [ ] **Support contact** email or URL

## ? Pricing & Availability

- [ ] **Markets selected** (where to distribute)
  - [ ] Worldwide
  - [ ] Or specific countries
- [ ] **Pricing model chosen**
  - [ ] Free
  - [ ] Paid (specify price)
  - [ ] Free with in-app purchases
- [ ] **Organizational licensing** (if enterprise)

## ? Package Build

- [ ] **Clean build completed**
  ```powershell
  .\Build-MSIXForStore.ps1
  ```
- [ ] **Build succeeded** without errors
- [ ] **Output files located**
  - [ ] .msixupload file found
  - [ ] .msixbundle file found

## ? Package Verification

- [ ] **Test installation locally**
  ```powershell
  Add-AppxPackage -Path "path\to\*.msixbundle"
  ```
- [ ] **App launches correctly** from Start Menu
- [ ] **All features work** as expected
- [ ] **Serial communication** works (if testable)
- [ ] **Network connectivity** works
- [ ] **File operations** work (Assets folder accessible)

## ? Submission

- [ ] **Logged into Partner Center**
- [ ] **New submission started**
- [ ] **Package uploaded** (.msixupload file)
- [ ] **Package validation passed** (automatic)
- [ ] **All sections completed**:
  - [ ] Properties
  - [ ] Pricing and availability
  - [ ] Store listings
  - [ ] Age ratings
  - [ ] Packages
- [ ] **Submission notes added** (if needed)
  - Explain special capabilities (serial communication)
  - Testing instructions for Microsoft reviewers
- [ ] **Final review completed**
- [ ] **Submitted to Store** ??

## ? Post-Submission

- [ ] **Certification status monitored** (24-48 hours typical)
- [ ] **Address any certification issues** if they arise
- [ ] **Test published app** from Store once approved
- [ ] **Monitor user feedback** and ratings
- [ ] **Plan for updates** and new versions

## ?? Important Notes

### Capabilities Declared
Your app declares these capabilities (already in Package.appxmanifest):
- ? Internet connectivity
- ? Private network access
- ? Removable storage
- ? Serial communication (USB devices)
- ? Full trust execution

**Important:** In submission notes, explain why serial communication is needed:
```
This app requires serial communication capability to connect with UAV flight controllers via USB for configuration and telemetry purposes.
```

### Version Management
- First submission: Use current version (1.0.11.0)
- Updates: Increment version number before each new submission
- Version format: Major.Minor.Build.Revision (e.g., 1.0.12.0)

### Common Rejection Reasons to Avoid
- [ ] Privacy policy missing or not accessible
- [ ] Screenshots don't match app functionality
- [ ] App crashes on launch
- [ ] Insufficient app description
- [ ] Capabilities not justified in submission notes

---

## ?? Need Help?

**Build Issues:** Check `MSIX-BUILD-GUIDE.md`  
**Quick Commands:** See `QUICK-BUILD-REFERENCE.md`  
**Partner Center Help:** https://partner.microsoft.com/support

---

**Last Updated:** 2024  
**Package Version:** 1.0.11.0  
**Target:** Microsoft Store Submission
