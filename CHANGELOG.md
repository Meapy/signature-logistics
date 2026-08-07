# Changelog

Full history for Signature Logistics. The `ChangeLog` field in
`Fix-Signatures/Properties/PublishConfiguration.xml` carries only the notes for the version being
published, so it always holds the topmost entry here and nothing else.

## 1.10.1 — unreleased

### Fixed

- **Conflict with [Change Company](https://github.com/rcav8tr/CS2Mod-ChangeCompany).** Its Company
  Workplaces override was overwritten within 64 frames and forced up to this mod's scaled ceiling, so
  the workplace count skyrocketed and could not be changed.

  1.10.0 re-asserted `WorkProvider.m_MaxWorkers` on every update, which is indistinguishable from
  fighting any other mod that manages workplaces.

  Change Company's `OverrideWorkplacesJob` query requires its own `WorkplacesOverride` component, so
  it only ever acts on buildings the player has explicitly given an override. This mod now skips
  exactly those companies and manages the rest, which is the same coexistence its readme describes
  for Realistic Workplaces and Households. Give a building a Company Workplaces override and it
  belongs to that mod; leave it without one and the worker capacity multiplier applies.

  A **Use Change Company for employees** option is also available, shown only when Change Company is
  detected and off by default, for anyone who would rather this mod never adjusted workplaces at all.

- **The worker capacity multiplier stalling well short of its setting.** A 10x setting could settle
  near 2x. The mod was tracking the exact workplace number it had written and standing down whenever
  the live value differed, but the company AI moves that number by one every tick, so it stood down
  within a tick or two of the first write. Reported as a Change Company conflict, but present without
  that mod installed.

## 1.10.0

### Added

- **Worker capacity and production multipliers, 1x to 10x**, as global defaults in Options and as
  per-building overrides in the info panel alongside the existing vehicle and storage sliders.
  - Workers scale `IndustrialProcessData.m_MaxWorkersPerCell`, which is what
    `CompanyUtils.GetIndustrialAndOfficeFittingWorkers` (`:42`) multiplies by lot size and level to
    reach the employment ceiling. This raises the ceiling only — hiring is still bounded by the
    city's labour supply and the company's profitability.
  - Production scales all three recipe stacks together. `ProcessingCompanySystem` derives input
    consumption as `input.m_Amount / output.m_Amount` (`:187`, `:193`), so scaling output alone would
    make the recipe cheaper rather than faster. **A 10x factory needs 10x the materials delivered**,
    so raise the storage and vehicle limits alongside it.
  - The two compound: 10x workers with 10x production is roughly 100x output, because workforce
    already feeds the production formula.
  - Raising the ceiling alone was not enough. `IndustrialAISystem` walks `WorkProvider.m_MaxWorkers`
    toward it one worker per update, and only while the company is fully staffed and under a quarter
    of its storage limit (`:145`, `:171-173`), so a 10x change would have taken hundreds of updates
    to appear. The tenant's worker count is now set straight to the scaled ceiling, and only ever
    upward — the game still sheds staff on its own terms.
  - Commercial signature tenants take their ceiling from `ServiceCompanyData.m_MaxWorkersPerCell`
    (`CompanyUtils.cs:37`) rather than the process, so that field is scaled too.

- **The Building logistics panel can be collapsed**, like Vehicles in use.

### Fixed

- The **Input restock target** row in Options showed its raw locale key instead of a label.

### Changed

- Per-building overrides now live in `SignatureBuildingSettings`, which writes a serialization
  version field, instead of `SignatureBuildingLimits`, which shipped without one and so could not
  gain the two multiplier fields without every existing save throwing
  `ComponentSerializerException: Data size mismatch`. The old component is no longer read.
  **Per-building overrides saved before this version are ignored** and those buildings fall back to
  the global defaults; global settings are unaffected.

## 1.0.9

### Fixed

- **Signature buildings losing their company, storage and employees on every reload.** This was a
  defect in 1.0.8 and it corrupted the affected companies in the save.

  The scoped company prefab copies introduced in 1.0.8 carry `Unity.Entities.Prefab` so the
  simulation cannot see them — but that also keeps them out of the table
  `BinaryWriter.Write(Entity)` remaps through, so it wrote `-1` for any tenant pointing at one
  (`BinaryWriter.cs:128-141`). On load, `-1` becomes `Entity.Null`, leaving the company with no
  prefab: no process, no workplaces, no storage limit, 0% efficiency.

  Scopes are now released before the city is written and re-applied immediately after, both inside
  the serialize phase, so saves only ever contain stock prefab references and no simulation frame
  runs with the vanilla limits.

  **Cities saved with 1.0.8 cannot be repaired automatically** — the information needed to identify
  each tenant's prefab was lost with the reference. Affected buildings log a warning and must be
  replaced to get a working tenant back.

## 1.0.8

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
- **Signature buildings losing their storage and employees on load, and becoming useless until
  replaced.** Scoped limits are now applied from `OnGameLoaded` and `OnGameLoadingComplete`, before
  the first simulation update, rather than up to one 64-frame update interval later.

  A save restores the tenant's `PrefabRef` to the authored prefab with its much lower vanilla storage
  limit, leaving the company over its limit the instant the city loads. Two vanilla systems then
  punish that state: `ProcessingCompanySystem` derives production from `storageLimit - storageUsed`
  without clamping at zero (`:220-224`), so the company produces a negative amount and deletes its
  own output; and `IndustrialAISystem` decrements `WorkProvider.m_MaxWorkers` whenever stock reaches
  half the limit (`:144`), so employees drain away too. The result was 0% efficiency, no income, and
  eventual bankruptcy.

  The load pass resolves tenants through `PropertyRenter`, not the building's `Renter` buffer, since
  `Renter` is rebuilt during deserialization and does not exist yet when the pass runs.

  Affected buildings recover on their own once the limits are correct — `IndustrialAISystem` raises
  `m_MaxWorkers` again while stock is below a quarter of the limit — but stock already destroyed does
  not come back.
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
