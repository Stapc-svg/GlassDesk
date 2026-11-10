@echo off
rem Builds GlassDesk.exe with the C# compiler that is already part of Windows (.NET Framework 4.x).
rem No downloads or installs needed.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:assets\GlassDesk.ico /resource:assets\logo.png,GlassDesk.logo.png /out:GlassDesk.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo Built GlassDesk.exe
start "" "%~dp0GlassDesk.exe"
