@echo off
:: Batch script to remove IdeaPadEcoManager from Windows Task Scheduler

echo [INFO] IdeaPadEcoManager eltavolitasa a Feladatutemezobol...
schtasks /delete /tn "IdeaPadEcoManager" /f

if %ERRORLEVEL% EQU 0 (
    echo [SIKER] A feladatutemezo bejegyzes sikeresen torolve!
) else (
    echo [INFO] A feladat nem letezett, vagy rendszergazdai jog szukseges a torlesehez.
)

pause
