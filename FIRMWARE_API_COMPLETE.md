# Firmware API - Complete Implementation ?

## Overview
All firmware upload, download, delete, and retrieval endpoints are now fully implemented and working.

## Fixed Issues
1. **HTTP Client Configuration**: Fixed duplicate registration of `FirmwareApiService` in `App.axaml.cs`
   - Removed duplicate `AddSingleton<FirmwareApiService>()` call
   - The service now properly uses the configured `HttpClient` with `BaseAddress` set

2. **Complete API Endpoints**: All endpoints are implemented in `FirmwareController.cs`

## API Endpoints

### 1. Health Check (Public)
```
GET /api/firmware/health
```
**Response:**
```json
{
  "status": "healthy",
  "message": "S3 bucket is accessible"
}
```

### 2. List Firmwares (Public)
```
GET /api/firmware/inapp
```
**Description**: Lists all firmware files available in S3 with presigned download URLs

**Response:**
```json
[
  {
    "key": "firmwares/arducopter_v1.0.0.apj",
    "fileName": "arducopter_v1.0.0.apj",
    "displayName": "ArduCopter",
    "vehicleType": "Copter",
    "size": 1048576,
    "sizeDisplay": "1.00 MB",
    "lastModified": "2025-02-10T06:20:06Z",
    "downloadUrl": "https://...",
    "firmwareName": "Custom Firmware",
    "firmwareVersion": "1.0.0",
    "firmwareDescription": "Description here"
  }
]
```

### 3. Upload Firmware (Admin)
```
POST /api/firmware/admin/upload
Content-Type: multipart/form-data
```

**Form Data:**
- `file`: Firmware file (.apj, .px4, .bin) - max 10MB
- `customFileName` (optional): Custom filename
- `firmwareName` (optional): Display name
- `firmwareVersion` (optional): Version string
- `firmwareDescription` (optional): Description

**Response:**
```json
{
  "key": "firmwares/custom_firmware.apj",
  "fileName": "custom_firmware.apj",
  "displayName": "Custom Firmware",
  "vehicleType": "Copter",
  "size": 1048576,
  "sizeDisplay": "1.00 MB",
  "lastModified": "2025-02-10T06:20:06Z",
  "downloadUrl": "https://...",
  "firmwareName": "Custom Firmware",
  "firmwareVersion": "1.0.0",
  "firmwareDescription": "My custom firmware"
}
```

### 4. Delete Firmware (Admin)
```
DELETE /api/firmware/admin/{key}
```
**Example:** `DELETE /api/firmware/admin/firmwares%2Ftest.apj`

**Response:**
```json
{
  "message": "Firmware deleted successfully"
}
```

### 5. Get Download URL
```
GET /api/firmware/download/{key}
```
**Example:** `GET /api/firmware/download/firmwares%2Ftest.apj`

**Response:**
```json
{
  "downloadUrl": "https://drone-config-param-logs.s3.ap-south-1.amazonaws.com/...",
  "expiresIn": 3600
}
```

## Service Layer Implementation

### FirmwareApiService (Client)
Located in: `PavamanDroneConfigurator.Infrastructure/Services/FirmwareApiService.cs`

**Methods:**
- `GetInAppFirmwaresAsync()` - Fetch firmware list
- `UploadFirmwareAsync()` - Upload firmware with metadata
- `DeleteFirmwareAsync()` - Delete firmware
- `DownloadFirmwareAsync()` - Download firmware file
- `GetDownloadUrlAsync()` - Get presigned URL
- `CheckHealthAsync()` - Health check

### AwsS3Service (Server)
Located in: `PavamanDroneConfigurator.Infrastructure/Services/AwsS3Service.cs`

**Methods:**
- `ListFirmwareFilesAsync()` - List all firmware files from S3
- `UploadFirmwareAsync()` - Upload firmware to S3 with metadata
- `DeleteFirmwareAsync()` - Delete firmware from S3
- `DownloadFirmwareAsync()` - Download firmware from S3
- `GeneratePresignedUrl()` - Generate presigned download URL
- `IsS3AccessibleAsync()` - Check S3 connectivity

## Configuration

### API Configuration (appsettings.json)
```json
{
  "AWS": {
    "Region": "ap-south-1",
    "BucketName": "drone-config-param-logs"
  }
}
```

### Desktop App Configuration (appsettings.json)
```json
{
  "Api": {
    "BaseUrl": "http://43.205.128.248:5000"
  }
}
```

## Testing

### 1. Test Health Endpoint
```powershell
curl http://43.205.128.248:5000/api/firmware/health
```

### 2. Test List Firmwares
```powershell
curl http://43.205.128.248:5000/api/firmware/inapp
```

### 3. Test Upload Firmware (with metadata)
```powershell
$boundary = [System.Guid]::NewGuid().ToString()
$headers = @{
    "Content-Type" = "multipart/form-data; boundary=$boundary"
}

$body = @"
--$boundary
Content-Disposition: form-data; name="file"; filename="test.apj"
Content-Type: application/octet-stream

[BINARY FILE CONTENT HERE]
--$boundary
Content-Disposition: form-data; name="firmwareName"

Test Firmware
--$boundary
Content-Disposition: form-data; name="firmwareVersion"

1.0.0
--$boundary
Content-Disposition: form-data; name="firmwareDescription"

Test firmware description
--$boundary--
"@

Invoke-RestMethod -Uri "http://43.205.128.248:5000/api/firmware/admin/upload" `
    -Method Post -Headers $headers -Body $body
```

### 4. Test Delete Firmware
```powershell
$key = [uri]::EscapeDataString("firmwares/test.apj")
curl -X DELETE "http://43.205.128.248:5000/api/firmware/admin/$key"
```

## Security Features

1. **IAM Role Authentication**: EC2 instance uses IAM role (no hardcoded credentials)
2. **Presigned URLs**: Direct S3 downloads without exposing credentials
3. **File Validation**: 
   - Only .apj, .px4, .bin files allowed
   - Max file size: 10MB
4. **Server-Side Encryption**: All S3 objects encrypted with AES256
5. **Metadata Support**: Custom firmware metadata stored securely

## Integration with Desktop App

The desktop app (`PavamanDroneConfigurator.UI`) uses `FirmwareApiService` to interact with the API:

```csharp
// Configured in App.axaml.cs
services.AddHttpClient<FirmwareApiService>(client =>
{
    var apiUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
        ?? Configuration?.GetValue<string>("Api:BaseUrl")
        ?? "http://localhost:5000";
    
    client.BaseAddress = new Uri(apiUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromMinutes(5);
});
```

## Production Deployment

### EC2 Instance Setup
1. EC2 instance has IAM role with S3 access
2. API running on port 5000
3. Security group allows inbound traffic on port 5000
4. S3 bucket: `drone-config-param-logs` in `ap-south-1`

### Environment Variables
```bash
# Not needed - uses IAM role
# AWS credentials are automatically provided by EC2 IAM role
```

## Troubleshooting

### Issue: "An invalid request URI was provided"
**Solution**: Fixed by removing duplicate `AddSingleton<FirmwareApiService>()` registration

### Issue: "Failed to load firmware list from cloud storage"
**Possible Causes:**
1. API server is down
2. Network connectivity issues
3. S3 bucket not accessible
4. IAM role misconfigured

**Debug Steps:**
1. Check health endpoint: `curl http://43.205.128.248:5000/api/firmware/health`
2. Check API logs on EC2 instance
3. Verify IAM role has S3 permissions
4. Test S3 access from EC2: `aws s3 ls s3://drone-config-param-logs/firmwares/`

## Next Steps

1. ? Health check endpoint
2. ? List firmwares endpoint
3. ? Upload firmware endpoint (with metadata)
4. ? Delete firmware endpoint
5. ? Download firmware endpoint (presigned URLs)
6. ? Desktop app integration
7. ? Fixed HTTP client configuration issue

All firmware management functionality is now complete and working!
