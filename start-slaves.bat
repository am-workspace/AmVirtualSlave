@echo off
echo ============================================================
echo  AmVritualSlave - Multi-Instance Launcher
echo ============================================================
echo.
echo  SlaveA: Modbus TCP :5020 (SlaveId=1) | OPC UA :4840
echo  SlaveB: Modbus TCP :5021 (SlaveId=2) | OPC UA :4841
echo.
echo  Press Ctrl+C in each window to stop, or close this window
echo  to stop all instances.
echo ============================================================
echo.

start "SlaveA - Port 5020/4840" cmd /k "dotnet run --project AmVritualSlave -- --config appsettings.SlaveA.json"
timeout /t 2 /nobreak >nul
start "SlaveB - Port 5021/4841" cmd /k "dotnet run --project AmVritualSlave -- --config appsettings.SlaveB.json"

echo.
echo Both instances launched. Close this window when done.
