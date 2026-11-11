param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$binary = Join-Path $OutputDirectory 'Byond-Mic-Patch.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$binary" "/win32manifest:$PSScriptRoot\app.manifest" "/resource:$PSScriptRoot\manifest.json,manifest.json" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Core.dll "$PSScriptRoot\src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
foreach ($name in @('Microphone-Patch.ps1','manifest.json','Install-Patch.cmd','Restore-Patch.cmd','Instruction.txt','LICENSE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $OutputDirectory -Force }
Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Built $binary"
