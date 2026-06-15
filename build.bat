@echo off
rem ============================================================
rem  Build script for TetraClip — compiles Program.cs into
rem  TetraClip.exe using the C# compiler that ships with Windows
rem  (.NET Framework 4.0). No SDK or internet required.
rem ============================================================
setlocal

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo Could not find the C# compiler ^(csc.exe^).
  echo Expected under C:\Windows\Microsoft.NET\Framework[64]\v4.0.30319\
  pause
  exit /b 1
)

set "ICONOPT="
if exist "icon.ico" set "ICONOPT=/win32icon:icon.ico"

echo Compiling TetraClip.exe ...
"%CSC%" /nologo /target:winexe /optimize+ %ICONOPT% /out:TetraClip.exe ^
  /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll ^
  Program.cs

if %errorlevel%==0 (
  echo.
  echo Build OK  -^>  TetraClip.exe
) else (
  echo.
  echo Build FAILED.
)
echo.
pause
endlocal
