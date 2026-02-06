# ?? Firmware Management API Endpoints

Complete API documentation for firmware management operations.

---

## ?? Endpoints

### 1. **List Firmwares** (Public)

**Endpoint:** `GET /api/firmware/inapp`

**Description:** Get list of all available firmwares with presigned download URLs

**Response:**
```json
[
  {
    "key": "firmwares/copter-4.5.2.apj",
    "fileName": "copter-4.5.2.apj",
    "displayName": "Copter 4.5.2",
    "vehicleType": "Copter",
    "size": 1048576,
    "sizeDisplay": "1.00 MB",
    "lastModified": "2024-01-15T10:30:00Z",
    "downloadUrl": "https://s3.amazonaws.com/presigned-url..."
  }
]
```

**Example:**
```bash
curl http://localhost:5000/api/firmware/inapp
```

---

### 2. **Upload Firmware** (Admin)

**Endpoint:** `POST /api/firmware/admin/upload`

**Description:** Upload a new firmware file to S3

**Headers:**
- `Content-Type: multipart/form-data`

**Form Data:**
- `file` (required): Firmware file (.apj, .px4, .bin)
- `customFileName` (optional): Custom filename

**Response:**
```json
{
  "key": "firmwares/custom-name.apj",
  "fileName": "custom-name.apj",
  "displayName": "Custom Name",
  "vehicleType": "Copter",
  "size": 1048576,
  "sizeDisplay": "1.00 MB",
  "lastModified": "2024-01-15T10:30:00Z",
  "downloadUrl": "https://s3.amazonaws.com/presigned-url..."
}
```

**Example:**
```bash
curl -X POST http://localhost:5000/api/firmware/admin/upload \
  -F "file=@firmware.apj" \
  -F "customFileName=copter-custom.apj"
```

**Validation:**
- Max file size: 10MB
- Allowed extensions: .apj, .px4, .bin

---

### 3. **Delete Firmware** (Admin)

**Endpoint:** `DELETE /api/firmware/admin/{key}`

**Description:** Delete a firmware file from S3

**URL Parameters:**
- `key`: S3 object key (e.g., `firmwares/copter-4.5.2.apj`)

**Response:**
```json
{
  "message": "Firmware deleted successfully"
}
```

**Example:**
```bash
curl -X DELETE http://localhost:5000/api/firmware/admin/firmwares%2Fcopter-4.5.2.apj
```

---

### 4. **Get Download URL**

**Endpoint:** `GET /api/firmware/download/{key}`

**Description:** Generate presigned download URL for a firmware file

**URL Parameters:**
- `key`: S3 object key

**Response:**
```json
{
  "downloadUrl": "https://s3.amazonaws.com/presigned-url...",
  "expiresIn": 3600
}
```

**Example:**
```bash
curl http://localhost:5000/api/firmware/download/firmwares%2Fcopter-4.5.2.apj
```

---

### 5. **Health Check**

**Endpoint:** `GET /api/firmware/health`

**Description:** Check S3 bucket connectivity

**Response:**
```json
{
  "status": "healthy",
  "message": "S3 bucket is accessible"
}
```

**Example:**
```bash
curl http://localhost:5000/api/firmware/health
```

---

## ?? Security

### Admin Endpoints

These endpoints should be protected by authentication:
- `POST /api/firmware/admin/upload`
- `DELETE /api/firmware/admin/{key}`

### Implementation

Add authorization attribute:
```csharp
[Authorize(Roles = "Admin")]
[HttpPost("admin/upload")]
public async Task<ActionResult<FirmwareMetadata>> UploadFirmware(...)
```

---

## ?? Client Usage (Desktop App)

### FirmwareApiService Methods

```csharp
// List firmwares
var firmwares = await _firmwareApi.GetInAppFirmwaresAsync();

// Upload firmware
var result = await _firmwareApi.UploadFirmwareAsync(
    filePath: @"C:\firmware.apj",
    customFileName: "copter-custom.apj",
    progress: new Progress<int>(p => Console.WriteLine($"Uploading: {p}%"))
);

// Download firmware
var localPath = await _firmwareApi.DownloadFirmwareAsync(
    downloadUrl: firmware.DownloadUrl,
    fileName: firmware.FileName,
    progress: new Progress<int>(p => Console.WriteLine($"Downloading: {p}%"))
);

// Delete firmware
var success = await _firmwareApi.DeleteFirmwareAsync(firmware.Key);

// Get download URL
var url = await _firmwareApi.GetDownloadUrlAsync(firmware.Key);
```

---

## ?? Testing

### Test Upload

```powershell
# Create test firmware file
echo "test firmware" > test.apj

# Upload
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/firmware/admin/upload" `
  -Form @{
    file = Get-Item "test.apj"
    customFileName = "test-firmware.apj"
  }
```

### Test Download

```powershell
# Get download URL
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/firmware/download/firmwares%2Ftest-firmware.apj"

# Download file
Invoke-WebRequest -Uri $response.downloadUrl -OutFile "downloaded.apj"
```

### Test Delete

```powershell
Invoke-RestMethod -Method Delete `
  -Uri "http://localhost:5000/api/firmware/admin/firmwares%2Ftest-firmware.apj"
```

---

## ?? Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 400 | Bad Request (invalid file, size limit exceeded) |
| 404 | Firmware not found |
| 500 | Server error (S3 connection failed) |
| 503 | Service unavailable (S3 not accessible) |

---

## ?? Configuration

### API (appsettings.Development.LOCAL.json)

```json
{
  "AWS": {
    "AccessKeyId": "YOUR_KEY",
    "SecretAccessKey": "YOUR_SECRET",
    "Region": "ap-south-1",
    "BucketName": "drone-config-param-logs"
  }
}
```

### Desktop App (appsettings.json)

```json
{
  "Api": {
    "BaseUrl": "http://localhost:5000"
  }
}
```

---

## ? Features Implemented

- ? List firmwares with presigned URLs
- ? Upload firmware with progress tracking
- ? Download firmware with progress tracking
- ? Delete firmware
- ? Generate download URLs
- ? Health check
- ? File validation (type, size)
- ? Error handling
- ? Logging

---

## ?? Deployment

### Local Testing

```bash
# Start API
cd PavamanDroneConfigurator.API
dotnet run

# Start Desktop App
cd PavamanDroneConfigurator.UI
dotnet run
```

### Production (EC2)

1. Set up IAM role on EC2 with S3 permissions
2. Deploy API
3. Update desktop app `appsettings.json` with EC2 IP
4. Distribute desktop app

---

## ?? Notes

- Presigned URLs expire after 1 hour
- Max upload size: 10MB
- Allowed file types: .apj, .px4, .bin
- Desktop app never stores AWS credentials
- All S3 operations go through API
