# PanoramaCommon.ps1
# Shared helpers for Backup-Panoramas.ps1, Restore-Panoramas.ps1 and Check-Panoramas.ps1.
# Dot-sourced by those scripts; not meant to be run on its own.
# Target: Windows PowerShell 5.1 (also works in PowerShell 7 on Windows).

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Repository layout. Tools\ sits at the repository root, next to Assets\.
$script:RepoRoot           = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:ToursRoot          = Join-Path $script:RepoRoot 'Assets\Tours'
$script:ManifestPath       = Join-Path $PSScriptRoot 'PanoramaManifest.csv'
$script:PanoramaExtensions = @('.jpg', '.jpeg', '.png')   # keep in step with .gitignore

# Native CreateHardLink: avoids New-Item's wildcard handling of [ ] in file names.
if (-not ('PanoramaTools.NativeMethods' -as [type])) {
    Add-Type -Namespace PanoramaTools -Name NativeMethods -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
'@
}

function Get-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-IsUnder([string]$Child, [string]$Parent) {
    $c = (Get-FullPath $Child) + '\'
    $p = (Get-FullPath $Parent) + '\'
    return $c.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeName([string]$Name, [string]$What) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -match '[\\/]' -or $Name -eq '.' -or $Name -eq '..') {
        throw "Invalid $What name in manifest: '$Name'"
    }
}

function Assert-MirrorRoot([string]$MirrorRoot) {
    if (Test-IsUnder $MirrorRoot $script:RepoRoot) {
        throw "MirrorRoot '$MirrorRoot' is inside the repository. Choose a folder outside it."
    }
}

# True when both paths are on the same NTFS volume (hard links possible).
function Test-HardLinkCapable([string]$PathA, [string]$PathB) {
    $rootA = [System.IO.Path]::GetPathRoot((Get-FullPath $PathA))
    $rootB = [System.IO.Path]::GetPathRoot((Get-FullPath $PathB))
    if ($rootA -ine $rootB) { return $false }
    $drive = New-Object System.IO.DriveInfo $rootA
    return ($drive.IsReady -and $drive.DriveFormat -eq 'NTFS')
}

function New-HardLink([string]$LinkPath, [string]$TargetPath) {
    if (-not [PanoramaTools.NativeMethods]::CreateHardLink($LinkPath, $TargetPath, [IntPtr]::Zero)) {
        $code = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        $msg  = (New-Object System.ComponentModel.Win32Exception $code).Message
        throw "CreateHardLink failed (${code}: $msg)`n  link:   $LinkPath`n  target: $TargetPath"
    }
}

# Puts $Source at $Destination, as a hard link or a copy. Destination must not exist.
function Set-PanoramaFile([string]$Source, [string]$Destination, [bool]$UseHardLink) {
    [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination))
    if ($UseHardLink) {
        New-HardLink -LinkPath $Destination -TargetPath $Source
    } else {
        Copy-Item -LiteralPath $Source -Destination $Destination   # preserves LastWriteTime
    }
}

# Two files count as the same panorama when size and last-write time match.
# A hard link always matches its target; Copy-Item preserves the time.
function Test-SameFile([System.IO.FileInfo]$A, [System.IO.FileInfo]$B) {
    return ($A.Length -eq $B.Length -and $A.LastWriteTimeUtc -eq $B.LastWriteTimeUtc)
}

# Every panorama image in Assets\Tours\<Tour>\Panoramas\ (any tour, current or future).
function Get-ProjectPanoramas {
    if (-not (Test-Path -LiteralPath $script:ToursRoot -PathType Container)) {
        throw "Tours folder not found: $script:ToursRoot"
    }
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($tour in Get-ChildItem -LiteralPath $script:ToursRoot -Directory) {
        $panoDir = Join-Path $tour.FullName 'Panoramas'
        if (-not (Test-Path -LiteralPath $panoDir -PathType Container)) { continue }
        foreach ($f in Get-ChildItem -LiteralPath $panoDir -File) {
            if ($script:PanoramaExtensions -contains $f.Extension.ToLowerInvariant()) {
                $list.Add([pscustomobject]@{
                    Tour = $tour.Name
                    File = $f.Name
                    Size = $f.Length
                    Info = $f
                })
            }
        }
    }
    return $list.ToArray()
}

function Get-ProjectPanoramaPath([string]$Tour, [string]$File) {
    return Join-Path (Join-Path (Join-Path $script:ToursRoot $Tour) 'Panoramas') $File
}

function Get-MirrorPath([string]$MirrorRoot, [string]$Tour, [string]$File) {
    return Join-Path (Join-Path $MirrorRoot $Tour) $File
}

function Get-Key([string]$Tour, [string]$File) {
    return ($Tour + '/' + $File).ToLowerInvariant()
}

function Read-Manifest {
    if (-not (Test-Path -LiteralPath $script:ManifestPath -PathType Leaf)) { return $null }
    $rows = @(Import-Csv -LiteralPath $script:ManifestPath -Encoding UTF8)
    foreach ($r in $rows) {
        Assert-SafeName $r.Tour 'tour'
        Assert-SafeName $r.File 'file'
    }
    return ,$rows
}

function Write-Manifest([object[]]$Items) {
    $sorted = $Items | Sort-Object -Property @{ Expression = { Get-Key $_.Tour $_.File } }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("Tour,File,Size`n")
    foreach ($i in $sorted) {
        $t = $i.Tour.Replace('"', '""')
        $f = $i.File.Replace('"', '""')
        [void]$sb.Append(('"{0}","{1}",{2}' -f $t, $f, $i.Size) + "`n")
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($script:ManifestPath, $sb.ToString(), $utf8NoBom)
}

# .meta files in Panoramas folders whose image is missing. Unity deletes these on import,
# and restoring the image afterwards gives it a new GUID, breaking NodeData references.
function Get-OrphanPanoramaMetas {
    $orphans = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $script:ToursRoot -PathType Container)) { return $orphans.ToArray() }
    foreach ($tour in Get-ChildItem -LiteralPath $script:ToursRoot -Directory) {
        $panoDir = Join-Path $tour.FullName 'Panoramas'
        if (-not (Test-Path -LiteralPath $panoDir -PathType Container)) { continue }
        foreach ($m in Get-ChildItem -LiteralPath $panoDir -File -Filter '*.meta') {
            $assetName = $m.Name.Substring(0, $m.Name.Length - 5)
            $ext = [System.IO.Path]::GetExtension($assetName).ToLowerInvariant()
            if ($script:PanoramaExtensions -contains $ext) {
                if (-not (Test-Path -LiteralPath (Join-Path $panoDir $assetName) -PathType Leaf)) {
                    $orphans.Add("$($tour.Name)/$assetName")
                }
            }
        }
    }
    return $orphans.ToArray()
}

function Write-List([string]$Title, [object[]]$Lines, [int]$Max = 25) {
    if ($Lines.Count -eq 0) { return }
    Write-Host ''
    Write-Host "$Title ($($Lines.Count)):"
    $Lines | Select-Object -First $Max | ForEach-Object { Write-Host "  $_" }
    if ($Lines.Count -gt $Max) { Write-Host "  ... and $($Lines.Count - $Max) more" }
}
