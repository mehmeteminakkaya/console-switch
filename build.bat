@echo off
rem Builds ConsoleSwitch.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:assets\icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  /out:dist\ConsoleSwitch.exe src\*.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built dist\ConsoleSwitch.exe
