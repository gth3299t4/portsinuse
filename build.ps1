# Builds PortsInUse.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK or Visual Studio needed. Output: .\PortsInUse.exe (single portable file).
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$src = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $PSScriptRoot 'PortsInUse.exe'
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'

& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32manifest:$manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out"
