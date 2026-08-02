# Publishing to Paradox Mods

Publishing is an external write. Build and verify first, then publish deliberately.

Mod ID `151747` — https://mods.paradoxplaza.com/mods/151747/Windows

## 1. Before the upload

- [ ] **`ModVersion`** bumped in `Fix-Signatures/Properties/PublishConfiguration.xml`, and **higher
      than what the listing already shows**. The publisher rejects a repeat with "Invalid
      UserModVersion, value must be different than the existing version's." Check the live version on
      the mod page rather than trusting this file — 1.0.7 was published without the bump ever being
      committed, so the repository read 1.0.6 while the listing was already at 1.0.7.
- [ ] **`ChangeLog`** contains **only** this version's notes. It is published verbatim against the
      version; the full history belongs in `CHANGELOG.md`.
- [ ] **`ShortDescription`** is 200 characters or fewer. The publisher rejects the upload with
      "Must be a string between 1 and 200 length" rather than truncating.
- [ ] **`GameVersion`** matches the installed game. Read it from the bottom of the main menu.
      Currently declared as `1.6.0*`. A mismatch puts an incompatibility warning on the listing.
- [ ] **Tags** — `Code Mod` is set. Accepted tags are server-side data; validate rather than
      trusting a stale value.
- [ ] Image filenames contain no spaces. The publisher fails with "Couldn't upload all files to the
      backend" if they do.

## 2. Build a clean release

The UI half goes first. A Release build hard-errors without the bundle, and `DeployWIP` clears the
deploy directory before copying.

```powershell
cd C:\Users\dkras\source\repos\Fix-Signatures\Fix-Signatures.UI
npm test

cd ..\Fix-Signatures
dotnet build -c Release
```

Require **0 errors**. Address any new warnings.

`DeployWIP` runs on `AfterBuild` and writes to `%CSII_LOCALMODSPATH%\Fix-Signatures`. Do not invoke
`-t:DeployWIP` directly — its condition reads `NeedBuild`, which only `BuildGetFullPaths` sets during
a normal build, so calling the target alone fails with `MSB4113`.

Exit the game before building. A running game locks the deployed files and the copy fails with
`MSB3231 Unable to remove directory ... Access to the path '..._win_x86_64.dll' is denied`.

## 3. Verify the package

```powershell
$stage = "$env:CSII_LOCALMODSPATH\Fix-Signatures"

Get-ChildItem -Recurse -File $stage | Get-FileHash -Algorithm SHA256 |
  Format-Table Hash, Path -AutoSize
```

The folder must contain all of:

- `Fix-Signatures.dll`
- `Fix-Signatures_win_x86_64.dll`, `_mac_x86_64.bundle`, `_linux_x86_64.so`
- `Fix-Signatures.mjs` and `Fix-Signatures.css`

Both UI files are expected here — the csproj copies them from `Fix-Signatures.UI/dist` as `Content`.
Their absence is not loud: the mod still loads and the interface is silently the old one.

Keep the hash list. The folder you upload must be the one you hashed.

If you have the skill's release verifier available:

```powershell
& 'C:\Users\dkras\.codex\skills\build-cities-skylines-2-mods\scripts\verify-release.ps1' `
  -ReleaseFolder $stage -AssemblyName Fix-Signatures -RequireUI
```

`-RequireUI` is correct for this mod, unlike CS2-TourismOverhaul, because a `.css` is emitted.

## 4. Test in game before publishing

A successful build does not prove anything ran.

- [ ] `Logs\SignatureFix.Mod.log` shows `OnLoad`
- [ ] Options → Signature Logistics renders and every slider moves
- [ ] Set a non-default vehicle or storage limit on a signature building, then select an unrelated
      zoned company of the same type and confirm it still shows vanilla caps
- [ ] Bulldoze the signature building and confirm its scoped prefab copy is released — the log
      reports the active scope count
- [ ] Save, reload, and confirm the scope is re-established and figures persist
- [ ] Override one building's limits, then reset, and confirm both take effect
- [ ] Remove the mod from a save and confirm the city still loads

## 5. Publish

Only after the checks above pass.

Run the official publisher directly rather than going through `dotnet publish`. The `.pubxml`
profiles in `Properties/PublishProfiles` are consumed by the Visual Studio and Rider Publish
dialogs; from the command line the profile is not imported, so `ModPublisherCommand` stays empty and
nothing uploads. The direct call also uploads exactly the folder you verified instead of whatever
the build happens to have left in the deploy directory.

`cd` into the managed project first: the media paths in `PublishConfiguration.xml` are relative
(`Properties/Thumbnail-signature-logistics.png`), and the publisher resolves them against the
working directory.

Use `$env:CSII_MODPUBLISHERPATH`, which points at the executable itself. It is **not** under
`CSII_TOOLPATH` — that variable points at the cached MSBuild toolchain, which contains no publisher.

```powershell
cd C:\Users\dkras\source\repos\Fix-Signatures\Fix-Signatures

& "$env:CSII_MODPUBLISHERPATH" NewVersion `
  'Properties\PublishConfiguration.xml' `
  -c "$env:CSII_LOCALMODSPATH\Fix-Signatures" -v
```

The publisher accepts exactly three commands:

- `Publish` — creates the listing. First upload only, while `ModId` is still empty.
- `NewVersion` — uploads a new package version to an existing listing. Requires `ModId`, and fails
  with "ModId must be set in configuration" without it.
- `Update` — metadata only: descriptions, screenshots, tags, links. No package is uploaded, so it
  cannot fix a bad build.

There is no `New`. `ModId` is already `151747`, so every release here uses `NewVersion`.

Authentication comes from the publisher's own Paradox session. Never pass credentials on the command
line or store them in the repository.

If the publisher reports the PDX account data file is missing, set `PDXAccountDataPath` in
`Fix-Signatures.csproj` to a credentials file kept **outside** the repository.

## 6. After publishing

- [ ] Record the returned mod ID and version
- [ ] Open https://mods.paradoxplaza.com/mods/151747/Windows and confirm it loads with the right
      description, changelog entry, thumbnail and links
- [ ] Confirm the GitHub link resolves: https://github.com/Meapy/signature-logistics
- [ ] Tag the release in git and push
- [ ] Confirm the working tree is clean
- [ ] Remove or rename the local deploy folder, or it shadows the subscribed copy in your own game

## Keeping things aligned

`PublishConfiguration.xml`'s long description, `README.md` and `CHANGELOG.md` should agree. When
bumping `ModVersion`, add the changelog entry in the same commit.

Never redistribute game assemblies, the toolchain, or Paradox credentials.
