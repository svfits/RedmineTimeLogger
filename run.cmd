@echo off
chcp 65001 >nul
setlocal
rem RedmineTimeLogger launcher. All arguments are passed to the app.
rem   run.cmd              interactive
rem   run.cmd --report     month report only
rem   run.cmd --help       help
rem Token comes from outside: REDMINE_API_KEY or REDMINE_API_KEY_FILE.

if "%REDMINE_API_KEY%"=="" if "%REDMINE_API_KEY_FILE%"=="" if not exist "%USERPROFILE%\.dsh\redmine-api-key" echo [!] REDMINE_API_KEY / REDMINE_API_KEY_FILE is not set.

dotnet run --project "%~dp0src\RedmineTimeLogger" -c Release -- %*
exit /b %ERRORLEVEL%
