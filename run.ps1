#!/usr/bin/env pwsh
# Запуск RedmineTimeLogger. Все аргументы передаются приложению как есть.
#
#   .\run.ps1                 интерактивный режим
#   .\run.ps1 --report        только отчёт по месяцу
#   .\run.ps1 --help          справка
#
# Ключ Redmine задаётся извне:
#   $env:REDMINE_API_KEY = "<ключ>"          либо
#   $env:REDMINE_API_KEY_FILE = "C:\...\key"

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\RedmineTimeLogger'

if (-not $env:REDMINE_API_KEY -and -not $env:REDMINE_API_KEY_FILE) {
    $fallback = Join-Path $env:USERPROFILE '.dsh\redmine-api-key'
    if (Test-Path $fallback) {
        Write-Host "Ключ берётся из файла $fallback" -ForegroundColor DarkGray
    }
    else {
        Write-Host 'Ключ Redmine не задан: установите REDMINE_API_KEY или REDMINE_API_KEY_FILE.' -ForegroundColor Yellow
    }
}

dotnet run --project $project -c Release -- @args
exit $LASTEXITCODE
