@echo off
rem GRAVEWARD SPIRAL のインストーラーを作る(ダブルクリックで動く)。
rem 版を変えるときは、引数で渡す: make_installer.cmd 0.3.0(省くと 0.2.1)
rem できたものは out\installer\GravewardSpiral-Setup-<版>.exe。Python と .NET 10 SDK と Inno Setup 6 が要る。
chcp 65001 > nul
setlocal
set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.2.1
set PYTHONUTF8=1
pushd "%~dp0..\.."
python tools\make_installer.py --version %VERSION%
set RESULT=%ERRORLEVEL%
popd
if not "%RESULT%"=="0" echo インストーラーを作れませんでした(上のメッセージを見てください)
pause
exit /b %RESULT%
