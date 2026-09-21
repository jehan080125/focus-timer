$ErrorActionPreference = 'Stop'
$timerSource = Join-Path $PSScriptRoot '..\dist\FocusTimer.exe'
if (!(Test-Path -LiteralPath $timerSource)) { throw 'Build dist/FocusTimer.exe first.' }
$timerInstall = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\FocusTimer'
$timerExe = Join-Path $timerInstall 'FocusTimer.exe'
$timerPrograms = Join-Path ([Environment]::GetFolderPath('Programs')) '집중 타이머.lnk'
$timerDesktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '집중 타이머.lnk'
New-Item -ItemType Directory -Path $timerInstall -Force | Out-Null
Copy-Item -LiteralPath $timerSource -Destination $timerExe -Force
$timerIcon = Join-Path $timerInstall 'FocusTimer-ios.ico'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\FocusTimer\Assets\Timer.ico') -Destination $timerIcon -Force

$timerShell = New-Object -ComObject WScript.Shell
foreach ($timerLinkPath in @($timerPrograms, $timerDesktop)) {
    $timerLink = $timerShell.CreateShortcut($timerLinkPath)
    $timerLink.TargetPath = $timerExe
    $timerLink.WorkingDirectory = $timerInstall
    $timerLink.IconLocation = "$timerIcon,0"
    $timerLink.Description = '집중 타이머'
    $timerLink.Save()
}

$timerUninstall = @'
$ErrorActionPreference = 'Stop'
$timerExpected = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\FocusTimer'
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($timerExpected)) { throw 'Unexpected installation directory.' }
$timerExe = Join-Path $timerExpected 'FocusTimer.exe'
try {
    Remove-Item -LiteralPath $timerExe -Force -ErrorAction Stop
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('타이머를 닫은 후 다시 제거해 주세요.', '집중 타이머') | Out-Null
    exit 1
}
foreach ($timerFolder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('DesktopDirectory'))) {
    $timerLinkPath = Join-Path $timerFolder '집중 타이머.lnk'
    if (Test-Path -LiteralPath $timerLinkPath) { Remove-Item -LiteralPath $timerLinkPath -Force }
}
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FocusTimer' -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $timerExpected 'FocusTimer-ios.ico') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $timerExpected 'Uninstall.ps1') -Force
if (@(Get-ChildItem -LiteralPath $timerExpected -Force).Count -eq 0) { Remove-Item -LiteralPath $timerExpected -Force }
'@
$timerUninstallPath = Join-Path $timerInstall 'Uninstall.ps1'
[IO.File]::WriteAllText($timerUninstallPath, $timerUninstall, [Text.UTF8Encoding]::new($true))
$timerRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FocusTimer'
New-Item -Path $timerRegistry -Force | Out-Null
$timerUninstallCommand = '"' + $env:SystemRoot + '\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $timerUninstallPath + '"'
foreach ($timerEntry in @{
    DisplayName = '집중 타이머'; DisplayVersion = '1.0.0'; DisplayIcon = "$timerIcon,0";
    InstallLocation = $timerInstall; UninstallString = $timerUninstallCommand
}.GetEnumerator()) {
    New-ItemProperty -Path $timerRegistry -Name $timerEntry.Key -Value $timerEntry.Value -PropertyType String -Force | Out-Null
}
New-ItemProperty -Path $timerRegistry -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $timerRegistry -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $timerRegistry -Name EstimatedSize -Value ([int]((Get-Item -LiteralPath $timerExe).Length / 1KB)) -PropertyType DWord -Force | Out-Null
Write-Output "Installed: $timerExe"
Write-Output "Start menu: $timerPrograms"
Write-Output "Desktop: $timerDesktop"
