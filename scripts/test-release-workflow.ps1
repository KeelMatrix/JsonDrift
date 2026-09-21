[CmdletBinding()]
param(
    [string]$WorkflowPath = (Join-Path $PSScriptRoot '..\.github\workflows\release.yml')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "Release workflow contract failed: $Message"
    }
}

$workflow = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $WorkflowPath).Path)
$validateJob = ([regex]::Match($workflow, '(?ms)^  validate:.*?(?=^  [a-z][a-z0-9-]*:|\z)')).Value
$publishJob = ([regex]::Match($workflow, '(?ms)^  publish:.*?(?=^  [a-z][a-z0-9-]*:|\z)')).Value
$releaseJob = ([regex]::Match($workflow, '(?ms)^  release:.*?(?=^  [a-z][a-z0-9-]*:|\z)')).Value
Assert-Contract (-not [string]::IsNullOrWhiteSpace($validateJob)) 'validate job is missing'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($publishJob)) 'publish job is missing'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($releaseJob)) 'release job is missing'
Assert-Contract ($workflow -match '(?ms)^on:\s*\r?\n\s+push:\s*\r?\n\s+tags:\s*\r?\n\s+- ''v\*''') 'workflow must be tag-push triggered'
Assert-Contract ($workflow -notmatch '(?m)^\s+(?:branches|pull_request|workflow_dispatch):') 'workflow must not expose ordinary push, pull-request, or manual triggers'
Assert-Contract ([regex]::Matches($workflow, '(?m)^\s+id-token:\s+write\s*$').Count -eq 1) 'exactly one job must request OIDC'
Assert-Contract ($publishJob -match '(?m)^\s+id-token:\s+write\s*$') 'publish job must request OIDC'
Assert-Contract ($validateJob -notmatch '(?m)^\s+id-token:\s+write\s*$') 'validation job must not request OIDC'
Assert-Contract ($releaseJob -match '(?m)^\s+contents:\s+write\s*$') 'release job must own contents write permission'
Assert-Contract ($publishJob -match 'NuGet/login@v1') 'publish job must use NuGet Trusted Publishing login'
Assert-Contract ($publishJob -match '(?m)^\s+user:\s+dmitriyzen\s*$') 'publish job must use NuGet.org user dmitriyzen'
Assert-Contract ($workflow -notmatch '(?i)secrets\.NUGET_API_KEY|secrets\[["'']NUGET_API_KEY') 'workflow must not use a long-lived NuGet API-key secret'
Assert-Contract ($workflow -match 'scripts/validate-release-version\.ps1') 'release workflow must rerun the release version contract'
Assert-Contract ($workflow -match 'scripts/validate\.ps1') 'release workflow must rerun the repository validation gate'
Assert-Contract ($publishJob -match '(?m)^\s+needs:\s+validate\s*$') 'publication must depend on validation'
Assert-Contract ($releaseJob -match '(?m)^\s+needs:\s+publish\s*$') 'GitHub Release creation must depend on successful publication'
Assert-Contract ($workflow -match '(?ms)dotnet\s+nuget\s+push.*?\.nupkg.*?--no-symbols') 'primary publication must submit only the validated package without an implicit symbol push'
Assert-Contract ($workflow -match '(?ms)dotnet\s+nuget\s+push.*?\.snupkg') 'symbol publication must submit the validated symbol archive'
Assert-Contract ($workflow -match '(?ms)gh\s+release\s+create') 'release job must create the GitHub Release only after publication'

Write-Output 'Release workflow contract passed.'
