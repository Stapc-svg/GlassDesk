@echo off
rem Builds GlassDesk with the C# compiler that is already part of Windows (.NET Framework 4.x).
rem No downloads or installs needed. Output: build\GlassDesk.exe
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  pause
  exit /b 1
)
if not exist build mkdir build
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:assets\GlassDesk.ico /out:build\GlassDesk.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
copy /y assets\GlassDesk.exe.config build\GlassDesk.exe.config >nul
echo Built build\GlassDesk.exe
