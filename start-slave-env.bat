@echo off
echo ============================================================
echo  AmVirtualSlave - Environment Variable Mode
echo ============================================================
echo.
echo  This demonstrates using AMVS_ environment variables
echo  to override appsettings.json without extra config files.
echo.
echo  Example: Starting SlaveA with env vars...
echo ============================================================
echo.

set AMVS_Modbus__Port=5020
set AMVS_Modbus__SlaveId=1
set AMVS_OpcUa__Port=4840
set AMVS_OpcUa__ApplicationName=AmVirtualSlave-A
set AMVS_Mqtt__TopicPrefix=industrial/slaveA

dotnet run --project AmVirtualSlave
