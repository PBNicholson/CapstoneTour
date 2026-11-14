<#
.SYNOPSIS
    Restores the panoramas listed in Tools\PanoramaManifest.csv from the panorama mirror
    into Assets\Tours\<Tour>\Panoramas\.

.DESCRIPTION
    Run on a fresh clone, or after checking out a different commit, BEFORE opening the
    project in Unity. If Unity opens the project while a panorama's image is missing, it
    deletes the panorama's .meta file; the image then gets a new GUID when it comes back,
    and every NodeData and Addressables reference to it breaks.

    - Missing panoramas are restored (hard link if the mirror is on the same NTFS drive,
      otherwise copied).
    - Panoramas already present and identical to the mirror are left alone.
    - Panoramas present but different from the mirror are reported, not overwritten,
      unless -Force is given.
    - Ends with a check for .meta files that still have no image.

    Use -WhatIf to preview without changing anything. Exit code 1 means a problem was found.

.PARAMETER MirrorRoot
    Mirror folder. Default: D:\VirtualTours\PanoramaMirror

.PARAMETER Copy
    Always copy instead of hard-linking.

.PARAMETER Force
    Overwrite panoramas in the project that differ from the mirror.

.EXAMPLE
    .\Tools\Restore-Panoramas.ps1 -WhatIf
    .\Tools\Restore-Panoramas.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$MirrorRoot = 'D:\VirtualTours\PanoramaMirror',
    [switch]$Copy,
    [switch]$Force
)

. (Join-Path $PSScriptRoot 'PanoramaCommon.ps1')

$MirrorRoot = Get-FullPath $MirrorRoot
Assert-MirrorRoot $MirrorRoot

$manifest = Read-Manifest
if ($null -eq $manifest) { throw "Manifest not found: $script:ManifestPath" }
if (-not (Test-Path -LiteralPath $MirrorRoot -PathType Container)) { throw "Mirror not found: $MirrorRoot" }

if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
    Write-Warning 'Unity is running. If THIS project is open in it, close Unity before restoring.'
}

$useHardLink = (-not $Copy) -and (Test-HardLinkCapable $script:RepoRoot $MirrorRoot)
Write-Host "Project:  $script:RepoRoot"
Write-Host "Mirror:   $MirrorRoot  ($(if ($useHardLink) { 'hard links' } else { 'copies' }))"
Write-Host "Manifest: $($manifest.Count) panoramas"

$restored    = New-Object System.Collections.Generic.List[string]
$overwritten = New-Object System.Collections.Generic.List[string]
$missing     = New-Object System.Collections.Generic.List[string]
$conflicts   = New-Object System.Collections.Generic.List[string]
$sizeDiffers = New-Object System.Collections.Generic.List[string]
$present     = 0

foreach ($e in $manifest) {
    $name = "$($e.Tour)/$($e.File)"
    $src  = Get-MirrorPath $MirrorRoot $e.Tour $e.File
    $dest = Get-ProjectPanoramaPath $e.Tour $e.File
    if (-not (Test-IsUnder $dest $script:ToursRoot)) { throw "Refusing to write outside Assets\Tours: $dest" }

    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { $missing.Add($name); continue }
    $srcInfo = Get-Item -LiteralPath $src
    if ($srcInfo.Length -ne [long]$e.Size) {
        $sizeDiffers.Add("$name  (manifest $($e.Size) bytes, mirror $($srcInfo.Length) bytes)")
    }

    if (Test-Path -LiteralPath $dest -PathType Leaf) {
        $destInfo = Get-Item -LiteralPath $dest
        if (Test-SameFile $destInfo $srcInfo) { $present++; continue }
        if (-not $Force) { $conflicts.Add($name); continue }
        if ($PSCmdlet.ShouldProcess($dest, 'Overwrite with mirror copy')) {
            Remove-Item -LiteralPath $dest -Force
            Set-PanoramaFile -Source $src -Destination $dest -UseHardLink $useHardLink
        }
        $overwritten.Add($name)
    } else {
        if ($PSCmdlet.ShouldProcess($dest, 'Restore from mirror')) {
            Set-PanoramaFile -Source $src -Destination $dest -UseHardLink $useHardLink
        }
        $restored.Add($name)
    }
}

$orphans = @()
if (-not $WhatIfPreference) { $orphans = @(Get-OrphanPanoramaMetas) }

Write-List 'Restored' $restored.ToArray() 10
Write-List 'Overwritten (-Force)' $overwritten.ToArray()
Write-List 'MISSING from mirror' $missing.ToArray()
Write-List 'Present but different from mirror (not touched; use -Force to overwrite)' $conflicts.ToArray()
Write-List 'Mirror differs from this commit (mirror holds a newer or older version)' $sizeDiffers.ToArray()
Write-List '.meta files with NO image (do not open Unity until resolved)' $orphans

Write-Host ''
Write-Host ("Summary: {0} restored, {1} already present, {2} overwritten, {3} missing, {4} conflicts, {5} orphan .meta." -f `
    $restored.Count, $present, $overwritten.Count, $missing.Count, $conflicts.Count, $orphans.Count)
if ($WhatIfPreference) { Write-Host 'WhatIf: nothing was changed.' }

if ($missing.Count -gt 0 -or $conflicts.Count -gt 0 -or $orphans.Count -gt 0) {
    Write-Host 'RESULT: problems found. Resolve them before opening the project in Unity.'
    exit 1
}
Write-Host 'RESULT: OK. Safe to open the project in Unity.'
exit 0
