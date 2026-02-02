@echo off
echo ===================================================
echo  PAVAMAN DRONE CONFIGURATOR - SKIP AUTH MODE
echo ===================================================
echo.
echo WARNING: Authentication is DISABLED
echo This should ONLY be used for development/testing
echo.
set SKIP_AUTH=true
dotnet run --project PavamanDroneConfigurator.UI
pause
