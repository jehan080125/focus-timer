$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$nativeOut = Join-Path $nativeRoot 'bin'
New-Item -ItemType Directory -Force $nativeOut | Out-Null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ x64 build tools are required.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$commands = @"
@echo off
chcp 65001 >nul
call "$vcvars" >nul
fxc /nologo /T vs_5_0 /E VS /Vn GlassVS /Fh GlassVS.h ..\Glass.hlsl
if errorlevel 1 exit /b 1
fxc /nologo /T ps_5_0 /E PS /Vn GlassPS /Fh GlassPS.h ..\Glass.hlsl
if errorlevel 1 exit /b 1
cl /nologo /LD /O2 /MT /EHsc /std:c++20 /DWIN32_LEAN_AND_MEAN /DNOMINMAX /I. ..\Glass.cpp /link /OUT:FocusTimer.Glass.dll d3d11.lib dxgi.lib windowsapp.lib user32.lib
"@
$script = Join-Path $nativeOut 'compile.cmd'
[System.IO.File]::WriteAllText($script, $commands, [System.Text.UTF8Encoding]::new($false))
Push-Location -LiteralPath $nativeOut
try {
    & cmd /c compile.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native glass renderer build failed.' }
} finally { Pop-Location }
