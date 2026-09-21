[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit,

    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail-Contract {
    param([Parameter(Mandatory = $true)][string]$Message)

    throw "Release version contract failed: $Message"
}

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        Fail-Contract $Message
    }
}

function Resolve-RepositoryFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $script:ResolvedRepositoryRoot $RelativePath
    Assert-Contract (Test-Path -LiteralPath $path -PathType Leaf) "required version-bearing file is missing: $RelativePath"
    return (Resolve-Path -LiteralPath $path).Path
}

function Get-SingleMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $matches = [regex]::Matches($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    Assert-Contract ($matches.Count -eq 1) "$Description must have exactly one match; found $($matches.Count)"
    return $matches[0]
}

function Assert-ExactVersionInFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $text = [IO.File]::ReadAllText($Path)
    $match = Get-SingleMatch -Text $text -Pattern $Pattern -Description $Description
    Assert-Contract ($match.Groups['version'].Value -ceq $ExpectedVersion) "$Description '$($match.Groups['version'].Value)' does not match '$ExpectedVersion'"
}

function Assert-InstallExamples {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = [IO.File]::ReadAllText($Path)
    $matches = [regex]::Matches(
        $text,
        '(?im)dotnet\s+add\s+package\s+KeelMatrix\.JsonDrift\s+--version(?:\s+|=)(?<version>[^\s`\\]+)')
    Assert-Contract ($matches.Count -gt 0) "no KeelMatrix.JsonDrift install example was found in $Path"
    foreach ($match in $matches) {
        $version = $match.Groups['version'].Value.Trim('"', "'", '`', ',', ')', ';')
        Assert-Contract ($version -ceq $ExpectedVersion) "install example in $Path uses '$version', expected '$ExpectedVersion'"
    }
}

function Assert-Changelog {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = [IO.File]::ReadAllText($Path)
    $headingPattern = "(?m)^##[ \t]+\[$([regex]::Escape($ExpectedVersion))\][ \t]+-[ \t]+(?<date>[^\r\n]+)[ \t]*$"
    $headingMatches = [regex]::Matches($text, $headingPattern)
    Assert-Contract ($headingMatches.Count -eq 1) "CHANGELOG.md must have exactly one finalized heading for '$ExpectedVersion'"
    $date = $headingMatches[0].Groups['date'].Value.Trim()
    Assert-Contract ($date -match '^\d{4}-\d{2}-\d{2}$') "CHANGELOG.md release heading for '$ExpectedVersion' must contain an ISO date"
    Assert-Contract ($date -notmatch '(?i)planned|unreleased|not[ \t-]+yet[ \t-]+published|tbd') "CHANGELOG.md release heading for '$ExpectedVersion' is not finalized"

    $sectionStart = $headingMatches[0].Index
    $nextHeading = [regex]::Match($text.Substring($sectionStart + $headingMatches[0].Length), '(?m)^##[ \t]+')
    $section = if ($nextHeading.Success) {
        $text.Substring($sectionStart, $headingMatches[0].Length + $nextHeading.Index)
    }
    else {
        $text.Substring($sectionStart)
    }
    Assert-Contract ($section -notmatch '(?i)\b(?:planned|unreleased|not[ \t-]+yet[ \t-]+published|tbd)\b') "CHANGELOG.md release section for '$ExpectedVersion' contains unfinished release language"
}

$script:ResolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$expectedCommitText = $ExpectedCommit.Trim()
Assert-Contract ($expectedCommitText -match '^[0-9a-fA-F]{40,64}$') 'expected commit must be a full hexadecimal Git object id'

$actualCommit = (& git -C $script:ResolvedRepositoryRoot rev-parse HEAD 2>&1 | Out-String).Trim()
Assert-Contract ($LASTEXITCODE -eq 0 -and $actualCommit -match '^[0-9a-fA-F]{40,64}$') 'could not resolve the checked-out commit'
Assert-Contract ($actualCommit.Equals($expectedCommitText, [StringComparison]::OrdinalIgnoreCase)) "checked-out commit '$actualCommit' does not match expected '$expectedCommitText'"

$buildProps = Resolve-RepositoryFile 'Directory.Build.props'
Assert-ExactVersionInFile -Path $buildProps -Pattern '<Version>(?<version>[^<]+)</Version>' -Description 'Directory.Build.props Version'

$projectFile = Resolve-RepositoryFile 'src/KeelMatrix.JsonDrift/KeelMatrix.JsonDrift.csproj'
$projectText = [IO.File]::ReadAllText($projectFile)
foreach ($match in [regex]::Matches($projectText, '(?m)<(?:Version|PackageVersion)>(?<version>[^<]+)</(?:Version|PackageVersion)>')) {
    Assert-Contract ($match.Groups['version'].Value -ceq $ExpectedVersion) "project version '$($match.Groups['version'].Value)' does not match '$ExpectedVersion'"
}

Assert-InstallExamples -Path (Resolve-RepositoryFile 'README.md')
Assert-InstallExamples -Path (Resolve-RepositoryFile 'src/KeelMatrix.JsonDrift/README.md')
Assert-Changelog -Path (Resolve-RepositoryFile 'CHANGELOG.md')

$relativeChangelog = 'CHANGELOG.md'
$trackedChangelog = (& git -C $script:ResolvedRepositoryRoot ls-files --error-unmatch -- $relativeChangelog 2>&1 | Out-String).Trim()
Assert-Contract ($LASTEXITCODE -eq 0 -and $trackedChangelog -eq $relativeChangelog) 'CHANGELOG.md must be tracked in the checked-out commit'
& git -C $script:ResolvedRepositoryRoot diff --quiet HEAD -- $relativeChangelog
Assert-Contract ($LASTEXITCODE -eq 0) "CHANGELOG.md differs from the supplied commit '$expectedCommitText'"

Write-Output "Release version contract passed: $ExpectedVersion at $actualCommit"
