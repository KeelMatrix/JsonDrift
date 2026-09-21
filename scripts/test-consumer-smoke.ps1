[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$root = Join-Path ([IO.Path]::GetTempPath()) "jsondrift-consumer-cache-regression-$([Guid]::NewGuid().ToString('N'))"
$poisonedCache = Join-Path $root 'poisoned-global-packages'
$poisonedPackage = Join-Path $poisonedCache 'keelmatrix.jsondrift/0.1.0'

try {
    New-Item -ItemType Directory -Path (Join-Path $poisonedPackage 'lib/net8.0') -Force | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $poisonedPackage 'KeelMatrix.JsonDrift.nuspec'),
        '<package><metadata><id>KeelMatrix.JsonDrift</id><version>0.1.0</version></metadata></package>',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $poisonedPackage '.nupkg.metadata'),
        '{"version":2,"contentHash":"poisoned"}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes(
        (Join-Path $poisonedPackage 'lib/net8.0/KeelMatrix.JsonDrift.dll'),
        [byte[]](0x70, 0x6f, 0x69, 0x73, 0x6f, 0x6e, 0x65, 0x64))

    $env:NUGET_PACKAGES = $poisonedCache
    $output = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'consumer-smoke.ps1') -PackagePath $resolvedPackage 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "consumer smoke did not bypass the poisoned inherited cache; exit code $exitCode"
    }

    $joined = $output -join [Environment]::NewLine
    if ($joined -notmatch 'inherited package cache ignored:' -or
        $joined -notmatch 'consumer package identity proof: candidate assembly sha256') {
        throw 'consumer smoke did not emit both cache-isolation and candidate-identity evidence'
    }

    Write-Output 'consumer cache regression: poisoned inherited cache ignored; candidate consumed'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
