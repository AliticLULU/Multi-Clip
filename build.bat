@echo off
rem ============================================================
rem  Build script for MemoClip — compiles the WPF project into
rem  MemoClip.exe using MSBuild which ships with Windows
rem  (.NET Framework 4.0+). No SDK or internet required.
rem ============================================================
setlocal

set "MSBUILD=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
if not exist "%MSBUILD%" set "MSBUILD=C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"

if not exist "%MSBUILD%" (
  echo Could not find MSBuild.exe.
  echo Expected under C:\Windows\Microsoft.NET\Framework[64]\v4.0.30319\
  pause
  exit /b 1
)

echo Building MemoClip.exe (WPF) ...
"%MSBUILD%" MemoClip.csproj /t:Build /p:Configuration=Release /v:minimal

if %errorlevel%==0 (
  echo.
  echo Build OK  -^>  MemoClip.exe
) else (
  echo.
  echo Build FAILED.
)
echo.
pause
endlocal
