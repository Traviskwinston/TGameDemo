$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Data'
$mono = Join-Path $unity 'Managed\UnityEngine'
$mgd = Join-Path $unity 'Managed'
$bcl = Join-Path $unity 'NetStandard\ref\2.1.0'

$refs = @()
$refs += (Get-ChildItem $mono -Filter '*.dll' | ForEach-Object { $_.FullName })
$refs += (Get-ChildItem $mono -Filter 'UnityEditor*.dll' | ForEach-Object { $_.FullName })
$p2 = Join-Path $mgd 'UnityEditor.dll'
if (Test-Path $p2) { $refs += $p2 }
$refs += (Get-ChildItem $bcl -Filter '*.dll' | ForEach-Object { $_.FullName })
$ui = Get-ChildItem (Join-Path $root 'Library\ScriptAssemblies') -Filter 'UnityEngine.UI.dll' -EA SilentlyContinue
if ($ui) { $refs += $ui.FullName } else { Write-Host 'WARN: UnityEngine.UI.dll not found' }
$refs = $refs | Sort-Object -Unique

$srcs = Get-ChildItem (Join-Path $root 'Assets\GameDemo') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }

$cscDll = Join-Path $unity 'DotNetSdkRoslyn\csc.dll'
$dotnet = Join-Path $unity 'NetCoreRuntime\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet).Source }

Write-Host "csc: $cscDll via $dotnet"
Write-Host "refs: $($refs.Count)  srcs: $($srcs.Count)"

$rsp = Join-Path $env:TEMP 'gd_compile.rsp'
$lines = @('-target:library', '-nologo', '-nostdlib+', '-langversion:9', '-define:UNITY_EDITOR;UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER', "-out:$env:TEMP\gd_check.dll")
foreach ($r in $refs) { $lines += "-r:`"$r`"" }
foreach ($s in $srcs) { $lines += "`"$s`"" }
Set-Content -Path $rsp -Value $lines -Encoding UTF8

& $dotnet $cscDll "@$rsp" 2>&1 | Select-String -Pattern 'error|warning' | Select-Object -First 40
Write-Host "EXIT=$LASTEXITCODE"
