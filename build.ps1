param([switch]$SkipUiVerification, [string]$OutputDirectory = 'artifacts/single-file')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$timerSdk = if (Test-Path '.cache/dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.cache/dotnet/dotnet.exe' } else { 'dotnet' }
$timerRestore = @('-p:NuGetAudit=false')
if (Test-Path '.cache/nuget/microsoft.netcore.app.ref.10.0.12.nupkg') {
    $timerRestore += @('--source', (Join-Path $PSScriptRoot '.cache/nuget'), '--packages', (Join-Path $PSScriptRoot '.cache/packages'))
}
function Invoke-TimerDotnet {
    & $timerSdk @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" }
}
Invoke-TimerDotnet restore FocusTimer.Tests/FocusTimer.Tests.csproj @timerRestore
Invoke-TimerDotnet run --project FocusTimer.Tests/FocusTimer.Tests.csproj -c Release --no-restore '-p:UseSharedCompilation=false'
Invoke-TimerDotnet restore FocusTimer/FocusTimer.csproj -r win-x64 '-p:SelfContained=true' '-p:PublishSingleFile=true' @timerRestore
Invoke-TimerDotnet publish FocusTimer/FocusTimer.csproj -c Release -r win-x64 --self-contained true --no-restore '-p:UseSharedCompilation=false' '-p:PublishSingleFile=true' '-p:IncludeAllContentForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=embedded' -o $OutputDirectory
if (!$SkipUiVerification) {
    $timerCheck = Start-Process -FilePath (Join-Path $OutputDirectory 'FocusTimer.exe') -ArgumentList '--verify-ui','artifacts/ui' -WindowStyle Hidden -PassThru -Wait
    if ($timerCheck.ExitCode -ne 0) { throw 'UI verification failed; see ui-verification-error.txt' }
    Get-Content -LiteralPath artifacts/ui/results.txt
}
New-Item -ItemType Directory -Path dist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'FocusTimer.exe') -Destination dist/FocusTimer.exe -Force
Write-Output 'Ready: dist/FocusTimer.exe'
