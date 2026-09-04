@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FRAMEWORK%\csc.exe" set "FRAMEWORK=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

if not exist "%FRAMEWORK%\csc.exe" (
    echo [ERROR] Windows .NET Framework C# compiler was not found.
    echo Expected:
    echo   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    echo or:
    echo   %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
    pause
    exit /b 1
)

echo Compiler:
echo   %FRAMEWORK%\csc.exe
echo.

if exist "SyncClipboardWin.exe" del /q "SyncClipboardWin.exe"

"%FRAMEWORK%\csc.exe" ^
  /nologo ^
  /langversion:5 ^
  /target:winexe ^
  /platform:anycpu ^
  /optimize+ ^
  /win32icon:"SyncClipboardWin\app.ico" ^
  /out:"SyncClipboardWin.exe" ^
  /reference:"%FRAMEWORK%\System.dll" ^
  /reference:"%FRAMEWORK%\System.Core.dll" ^
  /reference:"%FRAMEWORK%\System.Drawing.dll" ^
  /reference:"%FRAMEWORK%\System.Windows.Forms.dll" ^
  /reference:"%FRAMEWORK%\System.Web.Extensions.dll" ^
  /reference:"%FRAMEWORK%\System.IO.Compression.dll" ^
  /reference:"%FRAMEWORK%\System.IO.Compression.FileSystem.dll" ^
  /reference:"%FRAMEWORK%\Microsoft.CSharp.dll" ^
  SyncClipboardWin\*.cs

if errorlevel 1 (
    echo.
    echo [FAILED] Build failed.
    pause
    exit /b 1
)

echo.
echo [OK] Created:
echo   %CD%\SyncClipboardWin.exe
for %%F in ("SyncClipboardWin.exe") do echo   Size: %%~zF bytes
echo.
echo Portable deployment:
echo   SyncClipboardWin.exe
echo   config.json
echo.
pause
