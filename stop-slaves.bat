@echo off
echo ============================================================
echo  AmVirtualSlave - Stopping all instances
echo ============================================================
echo.

taskkill /FI "WINDOWTITLE eq SlaveA*" /T /F 2>nul
taskkill /FI "WINDOWTITLE eq SlaveB*" /T /F 2>nul

echo All slave instances stopped.
timeout /t 3 >nul
