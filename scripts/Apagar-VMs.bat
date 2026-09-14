@echo off
title OmniWin Lab - Apagado de Maquinas Virtuales
echo ========================================================
echo   OmniWin Lab - Guardando y Apagando VMs en Hyper-V
echo ========================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-VM | Where-Object { $_.Name -like 'OmniWin*' } | ForEach-Object { Write-Host 'Guardando ' $_.Name '...'; Stop-VM -VM $_ -Save -Force }; Write-Host 'Operacion completada. Memoria liberada con exito.' -ForegroundColor Green"
echo.
pause
