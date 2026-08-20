@echo off
rem Build DSHManager.exe with the in-box .NET Framework compiler (no SDK required).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: install .NET Framework 4.x Developer Pack or Visual Studio
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /utf8output /out:"%~dp0DSHManager.exe" /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll "%~dp0Program.cs"
exit /b %ERRORLEVEL%
