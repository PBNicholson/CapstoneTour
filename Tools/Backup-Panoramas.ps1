<#
.SYNOPSIS
    Mirrors every panorama in Assets\Tours\*\Panoramas\ to the panorama mirror and
    rewrites Tools\PanoramaManifest.csv.

.DESCRIPTION
    Run before committing whenever panoramas were added, re-shot or removed.
    - New panoramas are added to the mirror (hard link by default: no extra disk space).
    - Panoramas whose size or last-write time changed replace the mirror's copy.
    - Panoramas removed from the project are KEPT in the mirror. Nothing is ever deleted
      from the mirror except the old copy of a changed panorama.
    - Nothing inside the project is modified except Tools\PanoramaManifest.csv.

    Use -WhatIf to preview without changing anything.

.PARAMETER MirrorRoot
    Mirror folder. Default: D:\VirtualTours\PanoramaMirror

.PARAMETER Copy
    Make full copies instead of hard links (needed if the mirror is on another drive).

.EXAMPLE
    .\Tools\Backup-Panoramas.ps1 -WhatIf
    .\Tools\Backup-Panoramas.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$MirrorRoot = 'D:\VirtualTours\PanoramaMirror',
    [switch]$Copy
)

. (Join-Path $PSScriptRoot 'PanoramaCommon.ps1')

$MirrorRoot = Get-FullPath $MirrorRoot
Assert-MirrorRoot $MirrorRoot

$useHardLink = -not $Copy
if ($useHardLink -and -not (Test-HardLinkCapable $script:RepoRoot $MirrorRoot)) {
    throw "Hard links need the project and the mirror on the same NTFS drive. Use -Copy, or pick a mirror on the project's drive."
}

$items = @(Get-ProjectPanoramas)
Write-Host "Project:  $script:RepoRoot"
Write-Host "Mirror:   $MirrorRoot  ($(if ($useHardLink) { 'hard links' } else { 'copies' }))"
Write-Host "Found $($items.Count) panoramas in $(@($items | Select-Object -ExpandProperty Tour -Unique).Count) tours."

$added     = New-Object System.Collections.Generic.List[string]
$updated   = New-Object System.Collections.Generic.List[string]
$unchanged = 0

foreach ($p in $items) {
    $dest = Get-MirrorPath $MirrorRoot $p.Tour $p.File
    if (-not (Test-IsUnder $dest $MirrorRoot)) { throw "Refusing to write outside the mirror: $dest" }

    if (Test-Path -LiteralPath $dest -PathType Leaf) {
        $existing = Get-Item -LiteralPath $dest
        if (Test-SameFile $existing $p.Info) { $unchanged++; continue }

        if ($PSCmdlet.ShouldProcess($dest, 'Replace mirror copy with changed project file')) {
            Remove-Item -LiteralPath $dest -Force
            Set-PanoramaFile -Source $p.Info.FullName -Destination $dest -UseHardLink $useHardLink
        }
        $updated.Add("$($p.Tour)/$($p.File)")
    } else {
        if ($PSCmdlet.ShouldProcess($dest, 'Add to mirror')) {
            Set-PanoramaFile -Source $p.Info.FullName -Destination $dest -UseHardLink $useHardLink
        }
        $added.Add("$($p.Tour)/$($p.File)")
    }
}

# Mirror files no longer in the project (kept on purpose, reported only).
$projectKeys = @{}
foreach ($p in $items) { $projectKeys[(Get-Key $p.Tour $p.File)] = $true }
$mirrorOnly = New-Object System.Collections.Generic.List[string]
if (Test-Path -LiteralPath $MirrorRoot -PathType Container) {
    foreach ($tourDir in Get-ChildItem -LiteralPath $MirrorRoot -Directory) {
        foreach ($f in Get-ChildItem -LiteralPath $tourDir.FullName -File) {
            if (-not $projectKeys.ContainsKey((Get-Key $tourDir.Name $f.Name))) {
                $mirrorOnly.Add("$($tourDir.Name)/$($f.Name)")
            }
        }
    }
}

if ($PSCmdlet.ShouldProcess($script:ManifestPath, 'Write manifest')) {
    Write-Manifest -Items $items
}

Write-List 'Added to mirror' $added.ToArray()
Write-List 'Replaced in mirror (changed in project)' $updated.ToArray()
Write-List 'Kept in mirror, no longer in project' $mirrorOnly.ToArray()

Write-Host ''
Write-Host ("Summary: {0} added, {1} replaced, {2} unchanged, {3} mirror-only." -f $added.Count, $updated.Count, $unchanged, $mirrorOnly.Count)
if ($WhatIfPreference) {
    Write-Host 'WhatIf: nothing was changed.'
} else {
    Write-Host "Manifest written: $script:ManifestPath"
    Write-Host 'Commit Tools/PanoramaManifest.csv with the matching .meta files.'
}
