# Changelog

Full history for Signature Logistics. The `ChangeLog` field in
`Fix-Signatures/Properties/PublishConfiguration.xml` carries only the notes for the version being
published, so it always holds the topmost entry here and nothing else.

## 1.0.8 — unreleased

### Fixed

- **Settings applied to non-signature buildings.** The vehicle and storage limits were written onto
  the tenant's company prefab, which is shared by every company of that type in the city, so raising
  a limit for one signature building raised it for all commercial, office, and industrial companies
  using the same prefab. Each signature tenant now receives a private copy of its company prefab and
  only that company's `PrefabRef` is repointed at it. See `NOTES.md` for the read sites this was
  traced through and the traps involved in scoping it.
- The renter list was iterated as a live `DynamicBuffer` while the loop made structural changes. It
  is now copied to a temporary array first.
- Cached `ComponentLookup` and `BufferLookup` handles were reused after structural changes without
  being refreshed.
- Buildings taken from the query snapshot were used without checking they still existed, although
  the loop can destroy entities as it runs.
- **Random `NullReferenceException` dialog from the logger** ([#8]). The mod's log is now held open
  instead of being closed and reopened on every write, and the per-update counters log at Debug
  rather than Info. `UnityLogger.Open` swallows a failed reopen with a bare `catch` that leaves its
  stream writer null, and `Internal_WriteStream` then dereferences it without a null check, so any
  transient file lock surfaced as an exception dialog. Harmless to gameplay, but disruptive.

[#8]: https://github.com/Meapy/signature-logistics/issues/8

### Notes

- Nothing is written to the save. Affected cities need no migration: the incorrect prefab values
  were never serialized and clear when the game restarts.

## 1.0.7

Published to Paradox Mods without a matching commit — `PublishConfiguration.xml` in this repository
was still at 1.0.6 when 1.0.8 was prepared, so the notes for this release are not recorded here.
Copy them from the [listing](https://mods.paradoxplaza.com/mods/151747/Windows) to complete the
record.

## 1.0.6

### Fixed

- Repeated low-load buying. Empty outbound buying trucks now reserve their full capacity, preventing
  the same deficit from spawning a new request every 64 ticks.

### Changed

- Supplemental priority restocking now uses outside connections for dependable full loads. The
  game's ordinary local company purchases remain unchanged.

## 1.0.5 and earlier

Not recorded in this repository. The published version history is on the
[Paradox Mods listing](https://mods.paradoxplaza.com/mods/151747/Windows); paste those notes here to
complete the record.
