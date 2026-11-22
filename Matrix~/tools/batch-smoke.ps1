<#
.SYNOPSIS
Smoke-tests the batch entry (Feeder.MCP.Editor.MatrixBatch.Run) in a throw-away copy of the project setup.

.DESCRIPTION
Unity cannot open a project that another Editor has open, so this builds a small project next to it: it copies the
package manifest of -SourceProject, adds the Matrix package by path, starts Unity in batch mode with the batch entry,
waits for Temp/matrix-batch.ready.json, calls the MCP endpoint (initialize, tools/list, editor-health), then stops the
run through the quit file and prints the Unity exit code. The first run imports the project and takes minutes.

.EXAMPLE
pwsh -File Matrix~/tools/batch-smoke.ps1
pwsh -File Matrix~/tools/batch-smoke.ps1 -ProjectDir D:\Temp\mx-batch -KeepProject
#>
param(
    [string] $SourceProject = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string] $PackagePath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string] $ProjectDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'matrix-batch-smoke'),
    [string] $UnityExe,
    [int] $ReadyTimeoutMinutes = 20,
    [switch] $KeepProject
)

$ErrorActionPreference = 'Stop'
$version = ((Get-Content (Join-Path $SourceProject 'ProjectSettings\ProjectVersion.txt') | Select-Object -First 1) -split ':')[1].Trim()
if (-not $UnityExe) { $UnityExe = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path $UnityExe)) { throw "Unity $version not found at $UnityExe; pass -UnityExe." }

New-Item -ItemType Directory -Force (Join-Path $ProjectDir 'Assets'), (Join-Path $ProjectDir 'Packages'), (Join-Path $ProjectDir 'ProjectSettings') | Out-Null
Copy-Item (Join-Path $SourceProject 'ProjectSettings\ProjectVersion.txt') (Join-Path $ProjectDir 'ProjectSettings') -Force
$manifest = Get-Content -Raw (Join-Path $SourceProject 'Packages\manifest.json') | ConvertFrom-Json
$manifest.dependencies | Add-Member -NotePropertyName 'com.feeder.mcp' -NotePropertyValue ('file:' + ($PackagePath -replace '\\', '/')) -Force
# Unity rejects a BOM in manifest.json, and Windows PowerShell 5.1 adds one with -Encoding UTF8
[System.IO.File]::WriteAllText((Join-Path $ProjectDir 'Packages\manifest.json'), ($manifest | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding $false))

$ready = Join-Path $ProjectDir 'Temp\matrix-batch.ready.json'
$quit = Join-Path $ProjectDir 'Temp\matrix-batch.quit'
$log = Join-Path $ProjectDir 'batch-smoke.log'
Remove-Item -Force $ready, $quit -ErrorAction SilentlyContinue

$unity = Start-Process -FilePath $UnityExe -PassThru -ArgumentList @(
    '-batchmode', '-nographics', '-projectPath', "`"$ProjectDir`"", '-logFile', "`"$log`"",
    '-executeMethod', 'Feeder.MCP.Editor.MatrixBatch.Run', '-matrixIdleSeconds', '600', '-matrixReadyTimeoutSeconds', ($ReadyTimeoutMinutes * 60))
Write-Host "Unity $($unity.Id) started; log: $log"

$deadline = (Get-Date).AddMinutes($ReadyTimeoutMinutes + 1)
while (-not (Test-Path $ready)) {
    if ($unity.HasExited) { Get-Content $log -Tail 40; throw "Unity exited with $($unity.ExitCode) before Matrix was ready." }
    if ((Get-Date) -gt $deadline) { Stop-Process -Id $unity.Id -Force; throw 'Timed out waiting for the ready file.' }
    Start-Sleep -Seconds 3
}
$info = Get-Content -Raw $ready | ConvertFrom-Json
Write-Host "Ready: $($info.mcpUrl)"

function Invoke-Mcp($body, $session) {
    $headers = @{ Accept = 'application/json, text/event-stream' }
    if ($session) { $headers['Mcp-Session-Id'] = $session }
    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri $info.mcpUrl -ContentType 'application/json' -Headers $headers -Body $body
    $data = ($response.Content -split "`n" | Where-Object { $_ -like 'data:*' } | Select-Object -First 1) -replace '^data:\s*', ''
    [pscustomobject]@{ Session = $response.Headers['Mcp-Session-Id']; Body = if ($data) { $data | ConvertFrom-Json } else { $null } }
}

$init = Invoke-Mcp '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"batch-smoke","version":"1"}}}' $null
$session = $init.Session
[void](Invoke-Mcp '{"jsonrpc":"2.0","method":"notifications/initialized"}' $session)
$list = Invoke-Mcp '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' $session
Write-Host "tools/list: $($list.Body.result.tools.Count) tools"
$health = Invoke-Mcp '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"editor-health","arguments":{}}}' $session
Write-Host ($health.Body.result.content[0].text)

New-Item -ItemType File -Force $quit | Out-Null
if (-not $unity.WaitForExit(120000)) { Stop-Process -Id $unity.Id -Force; throw 'Unity did not exit after the quit file.' }
Write-Host "Unity exit code: $($unity.ExitCode) (0 expected)"
Select-String -Path $log -Pattern '\[MatrixBatch\]' | ForEach-Object { $_.Line }
if (-not $KeepProject) { Remove-Item -Recurse -Force $ProjectDir -ErrorAction SilentlyContinue }
