@echo off
rem Runs the local Release build (framework-dependent; needs the .NET 10 runtime under Y:\devtools\dotnet).
set DOTNET_ROOT=Y:\devtools\dotnet
start "" "%~dp0..\src\ClipNest.App\bin\x64\Release\net10.0-windows10.0.19041.0\ClipNest.exe" %*
