# EF Core Migration - Ready to Apply

## ? What's Been Created

### Migration Files
- `20260127055501_InitialCreate.cs` - Migration definition
- `20260127055501_InitialCreate.Designer.cs` - Metadata
- `DroneDbContextModelSnapshot.cs` - Current model snapshot

### Database Schema

#### Table: `drones`
- `id` (integer, PK, auto-increment)
- `serial_number` (varchar(50), unique index)
- `board_type` (integer, nullable)
- `board_name` (varchar(100), nullable)
- `firmware_version` (varchar(50), nullable)
- `vehicle_type` (varchar(50), nullable)
- `friendly_name` (varchar(200), nullable)
- `notes` (text, nullable)
- `created_at` (timestamp with time zone, default: CURRENT_TIMESTAMP)
- `last_connected_at` (timestamp with time zone, nullable)

#### Table: `parameter_history`
- `id` (integer, PK, auto-increment)
- `drone_id` (integer, FK to drones ? CASCADE)
- `parameter_name` (varchar(50), required)
- `parameter_value` (real/float)
- `changed_at` (timestamp with time zone, default: CURRENT_TIMESTAMP)
- `changed_by` (varchar(100), nullable)
- `change_reason` (varchar(500), nullable)
- **Index**: (drone_id, parameter_name, changed_at)

#### Table: `calibration_records`
- `id` (integer, PK, auto-increment)
- `drone_id` (integer, FK to drones ? CASCADE)
- `calibration_type` (varchar(50), required)
- `started_at` (timestamp with time zone, default: CURRENT_TIMESTAMP)
- `completed_at` (timestamp with time zone, nullable)
- `result` (varchar(20), required)
- `result_data` (text, nullable - JSON format)
- `notes` (text, nullable)
- `firmware_version` (varchar(50), nullable)
- **Index**: (drone_id, calibration_type, started_at)

## ?? How to Apply Migration

### Prerequisites
1. ? EF Core tools installed (dotnet-ef 9.0.0)
2. ? Migration files created
3. ?? **UPDATE PASSWORD** in `PavamanDroneConfigurator.UI/appsettings.json`
4. ?? Ensure network connectivity to RDS instance

### Apply Migration Command

```bash
# Run from solution root (C:\Pavaman\config\)
dotnet ef database update --project PavamanDroneConfigurator.Infrastructure --startup-project PavamanDroneConfigurator.UI --context DroneDbContext
```

### What This Will Do
1. Connect to PostgreSQL RDS using connection string from appsettings.json
2. Create `__EFMigrationsHistory` table (tracks migrations)
3. Execute the `InitialCreate` migration:
   - Create `drones` table
   - Create `parameter_history` table
   - Create `calibration_records` table
   - Create all indexes
   - Set up foreign key constraints

### Expected Output
```
Build started...
Build succeeded.
Applying migration '20260127055501_InitialCreate'.
Done.
```

### ?? Before Running
1. Open `PavamanDroneConfigurator.UI/appsettings.json`
2. Replace `YOUR_PASSWORD_HERE` with the actual database password
3. Verify you can reach the RDS endpoint:
   ```bash
   Test-NetConnection -ComputerName drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com -Port 5432
   ```

## ? Verification After Migration

### Option 1: Using psql
```bash
psql "host=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com port=5432 user=new_app_user dbname=drone_configurator sslmode=require"

# Inside psql:
\dt                                    # List all tables
\d drones                              # Describe drones table
\d parameter_history                   # Describe parameter_history table
\d calibration_records                 # Describe calibration_records table
SELECT * FROM "__EFMigrationsHistory"; # See applied migrations
```

### Option 2: Using Application
Run the DatabaseTestPage in your application - it will verify the connection works with the new schema.

### Option 3: Using EF Core Tools
```bash
dotnet ef migrations list --project PavamanDroneConfigurator.Infrastructure --startup-project PavamanDroneConfigurator.UI --context DroneDbContext
```

## ?? Rollback (if needed)

If something goes wrong, you can rollback:

```bash
# Remove the migration from database
dotnet ef database update 0 --project PavamanDroneConfigurator.Infrastructure --startup-project PavamanDroneConfigurator.UI --context DroneDbContext

# Remove the migration files
dotnet ef migrations remove --project PavamanDroneConfigurator.Infrastructure --startup-project PavamanDroneConfigurator.UI --context DroneDbContext
```

## ?? Common Issues

### Issue: "Unable to connect to database"
- **Solution**: Check password in appsettings.json
- **Solution**: Verify security group allows your IP on port 5432
- **Solution**: Check RDS instance is running and accessible

### Issue: "Table already exists"
- **Solution**: Database might have been partially created
- **Solution**: Drop existing tables manually or use a fresh database

### Issue: "SSL connection error"
- **Solution**: Connection string already includes `Ssl Mode=Require;Trust Server Certificate=true`
- **Solution**: Ensure your .NET version supports TLS 1.2+

## ?? Success Criteria

After running `dotnet ef database update`, you should have:
1. ? No error messages
2. ? Message "Done." at the end
3. ? Four tables in database:
   - `drones`
   - `parameter_history`
   - `calibration_records`
   - `__EFMigrationsHistory`
4. ? All indexes created
5. ? Foreign key constraints in place

---

**Ready to apply?** Update the password and run the command above!
