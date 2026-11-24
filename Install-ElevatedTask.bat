@echo off
:: Batch script to register IdeaPadEcoManager in Windows Task Scheduler with Highest Privileges
:: This allows automatic start at logon with full Administrator/WMI permissions without UAC prompt.

set EXE_PATH=%~dp0bin\Release\net8.0-windows\win-x64\publish\IdeaPadEcoManager.exe

if not exist "%EXE_PATH%" (
    echo [HIBA] A fajl nem talalhato: %EXE_PATH%
    echo Kerlek elobb forditsd le a projektet!
    pause
    exit /b 1
)

echo [INFO] IdeaPadEcoManager regisztralasa a Feladatutemezoben (Legmagasabb jogosultsag)...
schtasks /create /tn "IdeaPadEcoManager" /tr "\"%EXE_PATH%\" --minimized" /sc onlogon /rl highest /f

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SIKER] IdeaPadEcoManager sikeresen beallitva automatikus inditasra rendszergazdakent!
    echo Minden bejelentkezeskor automatikusan elindul a hatterben, UAC ertesites nelkul.
) else (
    echo.
    echo [HIBA] A regisztraciohoz futtasd ezt a bat fajlt Rendszergazdakent (Jobb klikk -> Futtatas rendszergazdakent)!
)

pause
