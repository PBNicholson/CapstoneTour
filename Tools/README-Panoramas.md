# Panorama images and git

Panorama images (`Assets/Tours/<Tour>/Panoramas/*.jpg|jpeg|png`) are **not stored in git**:
they total ~16 GB and grow with every shoot. Their **`.meta` files are stored in git**,
because `NodeData.panoramaTexture` (an `AssetReferenceCubemap`) and the Addressables groups
reference each panorama by the GUID in its `.meta`.

| What | Where |
|---|---|
| Source of truth | `Assets/Tours/<Tour>/Panoramas/` in the working project |
| Backup (mirror) | `D:\VirtualTours\PanoramaMirror\<Tour>\<file>` — NTFS hard links to the project files |
| What a commit expects | `Tools/PanoramaManifest.csv` (tour, file, size) — committed |

`D:\VirtualTours\TourImport` is a staging area for the importer, not part of this system.

## The one rule

**On a fresh clone, or after checking out a commit with different panoramas, run
`Restore-Panoramas.ps1` before opening the project in Unity.**

If Unity opens the project while a panorama image is missing, it deletes that panorama's
`.meta`. When the image comes back it gets a new GUID, and every node and Addressables
entry that pointed to it breaks.

If that happens on a fresh clone: close Unity, run
`git restore -- Assets/Tours Assets/AddressableAssetsData` (brings back the deleted `.meta`
files and any Addressables group the editor rewrote), run `Restore-Panoramas.ps1`, then
reopen Unity. Do not run that `git restore` in a working copy with uncommitted changes in
those folders: it discards them.

## Running the scripts

From the repository root, in Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Check-Panoramas.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Backup-Panoramas.ps1 -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Backup-Panoramas.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Restore-Panoramas.ps1
```

`-ExecutionPolicy Bypass` applies to that one run only. `Backup` and `Restore` accept
`-WhatIf` to preview, `-MirrorRoot <path>` to use another mirror, and `-Copy` to make full
copies instead of hard links. `Restore` also accepts `-Force` to overwrite panoramas that
differ from the mirror.

| Script | Changes | Use |
|---|---|---|
| `Check-Panoramas.ps1` | nothing | Before committing. Exit code 1 = backup needed or `.meta` problem. |
| `Backup-Panoramas.ps1` | mirror, `PanoramaManifest.csv` | After adding, re-shooting or removing panoramas. |
| `Restore-Panoramas.ps1` | `Assets/Tours/*/Panoramas/` images only | Fresh clone or checkout, **before Unity opens**. |

## Workflow when adding or re-shooting panoramas

1. Import the new tour or panoramas into Unity as usual (Unity creates the `.meta` files).
2. Run `Backup-Panoramas.ps1`.
3. Run `Check-Panoramas.ps1`; it should report OK.
4. Commit the new `.meta` files, the tour/node assets, the Addressables changes **and**
   `Tools/PanoramaManifest.csv` together.

New tour folders are picked up automatically; there is no per-tour configuration.

## Limits of this setup

- **The mirror is on the same physical drive as the project.** It protects against
  accidental deletion, `git clean -x`, and fresh clones. It does **not** protect against
  the drive failing. Copy the mirror to another drive or cloud storage when one is available
  (`Backup-Panoramas.ps1 -MirrorRoot <other drive> -Copy` makes full copies there).
- **Hard links share data.** Editing a JPG in place changes the mirror too. Re-shoots
  should *replace* the file (new file, same name), which the backup detects by size and
  modified time.
- **The mirror keeps only the latest version of each file name.** Checking out an older
  commit restores the current image for each name; `Restore-Panoramas.ps1` lists any whose
  size differs from that commit's manifest.
- **The mirror never deletes.** Panoramas removed from the project stay in the mirror
  (reported by `Backup` as "mirror-only"). Delete them by hand once no commit you care
  about needs them.
