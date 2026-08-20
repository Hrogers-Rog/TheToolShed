<#
.SYNOPSIS
    Mirrors Toolshed's user-facing docs into its GitHub Wiki.

.DESCRIPTION
    The repository docs are the source of truth. The generated wiki pages are
    replaced on each sync so fixes cannot drift between two copies.
#>
[CmdletBinding()]
param(
    [string] $WikiUrl = 'https://github.com/Hrogers-Rog/TheToolShed.wiki.git',
    [string] $WorkDir = (Join-Path $env:TEMP 'toolshed-wiki-sync'),
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$docsDir = Join-Path $repoRoot 'docs'
$blobBase = 'https://github.com/Hrogers-Rog/TheToolShed/blob/main'

$pageMap = [ordered]@{
    'GETTING_STARTED.md'       = 'Getting-Started'
    'SERVICE_FACILITIES.md'    = 'Service-Facilities'
    'OIL_WOOD_FIRING.md'       = 'Oil-And-Wood-Firing'
    'LINK_AND_PIN.md'          = 'Link-And-Pin-Couplers'
    'SELECTIVE_INTERCHANGES.md' = 'Selective-Interchanges'
}

function Convert-Links {
    param([string] $Text)

    $Text = [regex]::Replace($Text, '\]\(\.\./([^)#]+)(#[^)]*)?\)', {
        param($match)
        "]($blobBase/$($match.Groups[1].Value)$($match.Groups[2].Value))"
    })
    $Text = [regex]::Replace($Text, '\]\(([A-Za-z0-9_]+\.md)(#[^)]*)?\)', {
        param($match)
        $file = $match.Groups[1].Value
        $anchor = $match.Groups[2].Value
        if ($pageMap.Contains($file)) { "]($($pageMap[$file])$anchor)" }
        else { "]($blobBase/docs/$file$anchor)" }
    })
    return $Text
}

Write-Host 'Toolshed wiki sync' -ForegroundColor Cyan

if (Test-Path -LiteralPath $WorkDir) {
    git -C $WorkDir fetch --quiet origin
    if ($LASTEXITCODE -ne 0) { throw "Could not update wiki checkout at $WorkDir." }
    git -C $WorkDir reset --quiet --hard origin/master
}
else {
    git clone --quiet $WikiUrl $WorkDir
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone $WikiUrl. Create and save the wiki's first page in GitHub, then retry."
    }
}

Get-ChildItem -LiteralPath $WorkDir -Filter '*.md' -File | Remove-Item -Force

$copied = 0
foreach ($entry in $pageMap.GetEnumerator()) {
    $source = Join-Path $docsDir $entry.Key
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        Write-Warning "Missing source doc: $($entry.Key)"
        continue
    }

    $body = Convert-Links (Get-Content -Raw -LiteralPath $source)
    $footer = "`n`n---`n`n*Mirrored from [``docs/$($entry.Key)``]($blobBase/docs/$($entry.Key)) - edit the repository copy.*`n"
    Set-Content -LiteralPath (Join-Path $WorkDir "$($entry.Value).md") -Value ($body.TrimEnd() + $footer) -Encoding utf8
    $copied++
}

$homePage = @"
# Toolshed

Toolshed is an optional FUSE companion for working service facilities,
alternative steam fuels, selective interchanges, period couplers, and hand
turntables. FUSE Core remains the required loader; the Tile Editor is only an
authoring tool and is not required by players.

## Start Here

- [Getting Started](Getting-Started)
- [Service Facilities](Service-Facilities)
- [Oil And Wood Firing](Oil-And-Wood-Firing)
- [Link And Pin Couplers](Link-And-Pin-Couplers)
- [Selective Interchanges](Selective-Interchanges)

## Package Author Examples

- [Service facility setup guide]($blobBase/Examples/service-facility-setup-guide.md)
- [Service loader component blueprint]($blobBase/Examples/service-loader-component-blueprint.md)
- [Selective interchange example]($blobBase/Examples/selective-interchange-readme.md)

## Project

- [Repository](https://github.com/Hrogers-Rog/TheToolShed)
- [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup)

---

*This wiki is generated from ``docs/``. Repository changes overwrite direct
wiki edits on the next sync.*
"@
Set-Content -LiteralPath (Join-Path $WorkDir 'Home.md') -Value $homePage -Encoding utf8

$sidebar = @"
**[Toolshed](Home)**

**Players And Authors**

- [Getting Started](Getting-Started)
- [Service Facilities](Service-Facilities)
- [Oil And Wood Firing](Oil-And-Wood-Firing)
- [Link And Pin Couplers](Link-And-Pin-Couplers)
- [Selective Interchanges](Selective-Interchanges)
"@
Set-Content -LiteralPath (Join-Path $WorkDir '_Sidebar.md') -Value $sidebar -Encoding utf8

Write-Host "Wrote $copied page(s) plus Home and _Sidebar." -ForegroundColor Green

Push-Location $WorkDir
try {
    if (-not (git status --porcelain)) {
        Write-Host 'Wiki already up to date.' -ForegroundColor Green
        return
    }

    git -c color.status=always status --short
    if ($DryRun) {
        Write-Host 'Dry run - nothing committed or pushed.' -ForegroundColor Yellow
        return
    }

    $sha = git -C $repoRoot rev-parse --short HEAD
    git add -A
    git commit --quiet -m "docs: sync wiki from repo @ $sha"
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit generated wiki pages.' }
    git push --quiet origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not push generated wiki pages.' }
    Write-Host "Pushed wiki update (source $sha)." -ForegroundColor Green
}
finally {
    Pop-Location
}
