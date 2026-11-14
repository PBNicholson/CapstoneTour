<#
.SYNOPSIS
    Read-only check: are all panoramas backed up, and does Tools\PanoramaManifest.csv
    match the project?

.DESCRIPTION
    Run before committing. Changes nothing. Exit code 1 means Backup-Panoramas.ps1
    needs to be run (or a .meta problem needs attention).

.PARAMETER MirrorRoot
    Mirror folder. Default: D:\VirtualTours\PanoramaMirror

.EXAMPLE
    .\Tools\Check-Panoramas.ps1
#>
[CmdletBinding()]
param(
    [string]$MirrorRoot = 'D:\VirtualTours\PanoramaMirror'
)

. (Join-Path $PSScriptRoot 'PanoramaCommon.ps1')

$MirrorRoot = Get-FullPath $MirrorRoot
$items    = @(Get-ProjectPanoramas)
$manifest = Read-Manifest

Write-Host "Project:  $script:RepoRoot"
Write-Host "Mirror:   $MirrorRoot"
Write-Host "Found $($items.Count) panoramas in the project."

# 1. Backup state
$notBackedUp = New-Object System.Collections.Generic.List[string]
foreach ($p in $items) {
    $m = Get-MirrorPath $MirrorRoot $p.Tour $p.File
    if (-not (Test-Path -LiteralPath $m -PathType Leaf)) {
        $notBackedUp.Add("$($p.Tour)/$($p.File)  (not in mirror)")
    } elseif (-not (Test-SameFile (Get-Item -LiteralPath $m) $p.Info)) {
        $notBackedUp.Add("$($p.Tour)/$($p.File)  (changed since last backup)")
    }
}

# 2. Manifest vs project
$manifestProblems = New-Object System.Collections.Generic.List[string]
if ($null -eq $manifest) {
    $manifestProblems.Add("Manifest not found: $script:ManifestPath")
} else {
    $byKey = @{}
    foreach ($e in $manifest) { $byKey[(Get-Key $e.Tour $e.File)] = $e }
    $seen = @{}
    foreach ($p in $items) {
        $k = Get-Key $p.Tour $p.File
        $seen[$k] = $true
        if (-not $byKey.ContainsKey($k)) {
            $manifestProblems.Add("$($p.Tour)/$($p.File)  (in project, not in manifest)")
        } elseif ([long]$byKey[$k].Size -ne $p.Size) {
            $manifestProblems.Add("$($p.Tour)/$($p.File)  (size differs from manifest)")
        }
    }
    foreach ($e in $manifest) {
        if (-not $seen.ContainsKey((Get-Key $e.Tour $e.File))) {
            $manifestProblems.Add("$($e.Tour)/$($e.File)  (in manifest, not in project)")
        }
    }
}

# 3. .meta consistency
$orphans = @(Get-OrphanPanoramaMetas)
$noMeta  = @($items | Where-Object { -not (Test-Path -LiteralPath ($_.Info.FullName + '.meta') -PathType Leaf) } |
             ForEach-Object { "$($_.Tour)/$($_.File)" })

Write-List 'Not backed up' $notBackedUp.ToArray()
Write-List 'Manifest out of date' $manifestProblems.ToArray()
Write-List '.meta files with NO image' $orphans
Write-List 'Images with no .meta yet (open Unity to import them, then back up)' $noMeta

Write-Host ''
if ($notBackedUp.Count -gt 0 -or $manifestProblems.Count -gt 0) {
    Write-Host 'RESULT: run .\Tools\Backup-Panoramas.ps1, then commit Tools/PanoramaManifest.csv.'
    exit 1
}
if ($orphans.Count -gt 0) {
    Write-Host 'RESULT: backups are current, but some .meta files have no image (see above).'
    exit 1
}
Write-Host 'RESULT: OK. All panoramas are backed up and the manifest is current.'
exit 0
