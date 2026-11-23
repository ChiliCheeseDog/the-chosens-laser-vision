param(
    [Parameter(Mandatory = $true)][string]$ReferenceDirectory,
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build')
)
$ErrorActionPreference = 'Stop'
$referenceRoot = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$target = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $target | Out-Null
$assembly = Join-Path $target 'TheChosensLaserVision.dll'
$response = Join-Path $target 'compile.rsp'
$arguments = @('/nologo', '/target:library', '/optimize+', '/deterministic+', '/langversion:7.3', '/nostdlib+', ('/out:"' + $assembly + '"'))
foreach ($name in @('mscorlib','System','System.Core','netstandard','ThunderRoad','UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.AudioModule','UnityEngine.AnimationModule','UnityEngine.ParticleSystemModule','UnityEngine.ImageConversionModule','Newtonsoft.Json','ThunderRoad.Manikin','ThunderRoad.Reveal','ThunderRoad.MaterialInstance')) {
    $reference = Join-Path $referenceRoot ($name + '.dll')
    if (!(Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference" }
    $arguments += '/reference:"' + $reference + '"'
}
$arguments += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object { '"' + $_.FullName + '"' }
$arguments | Set-Content -LiteralPath $response -Encoding UTF8
if ([IO.Path]::GetExtension($compiler) -eq '.dll') {
    & dotnet $compiler /noconfig ('@' + $response)
} else {
    & $compiler /noconfig ('@' + $response)
}
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
Remove-Item -LiteralPath $response
Write-Output "Built $assembly"
