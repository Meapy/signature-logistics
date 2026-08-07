# Notes

Findings that cost real debugging time. The changelog says what changed; this says why it was hard.

All file and line citations below are against the decompiled game source at `F:\CS2Decompiled`.

## Settings leaked to every commercial / office / industrial company (fixed)

**Symptom.** Players reported the mod's vehicle and storage settings applying to all zoned
commercial, office and industrial buildings rather than only signature buildings.

**Cause.** `SignatureFixSystem.OnUpdate` resolved the tenant's company prefab and wrote the limits
straight onto it:

```csharp
Entity companyPrefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
EntityManager.SetComponentData(companyPrefab, transportCompany); // m_MaxTransports
EntityManager.SetComponentData(companyPrefab, storageLimit);     // m_Limit
```

A signature building's renter is an ordinary company using a stock company prefab, and that prefab
is shared by every company of the same type in the city. One signature building hosting, say, a food
commercial company raised the caps for every food commercial company citywide.

**Why an instance-level override could not work.** Both components are prefab-level:

- `Game.Prefabs.StorageLimit.GetPrefabComponents` adds `StorageLimitData`; `Initialize` sets `m_Limit`.
- `Game.Prefabs.ProcessingCompany.GetPrefabComponents` adds `TransportCompanyData` when `transports > 0`.

Every consumer reads them as `ComponentLookup<T>[PrefabRef.m_Prefab]` of a **company** entity, never
from the company itself:

- `Game.Simulation/IndustrialAISystem.cs:118-119`
- `Game.Simulation/IndustrialDemandSystem.cs:276-281`
- `Game.Simulation/BuyingCompanySystem.cs:385`
- `Game.Simulation/TripNeededSystem.cs:355-359` via
  `Game.Vehicles/VehicleUtils.cs:2381` `GetTransportCompanyAvailableVehicles`

So adding a component to the company entity would simply never be read.

**Fix.** Each signature tenant gets a private copy of its company prefab
(`SignaturePrefabScope`), and only that company's `PrefabRef` is repointed at the copy.

## Traps found while implementing the scoped copy

**CS2 prefab entities do not carry the Unity `Prefab` tag.** `PrefabSystem.AddPrefab`
(`Game.Prefabs/PrefabSystem.cs:145`) builds them with a plain `EntityManager.CreateEntity` over the
component set from `GetPrefabComponents`. They are therefore visible to ordinary queries — which is
exactly how `CommercialSpawnSystem.m_CommercialCompanyPrefabGroup`
(`Game.Simulation/CommercialSpawnSystem.cs:179`) enumerates company prefabs and picks one at random
for a new company.

This makes a naive clone actively dangerous: the copy would join the spawn pool and get handed to
ordinary zoned buildings, reintroducing the leak in a worse form, and would skew prefab-side counts.

The escape is to add `Unity.Entities.Prefab` to the copy. `EntityQueryOptions.IncludePrefab` does not
appear anywhere in the decompiled game (only `IncludeSystems` does), so the tag reliably hides the
copy from every vanilla query, while `ComponentLookup<T>[entity]` reads ignore query filters and
still resolve it.

**Nothing reaches the save.** `PrefabRef` is written through
`Game.Serialization/PrefabReferences.cs:61` `Check`, which encodes `PrefabData.m_Index`. `Instantiate`
copies `PrefabData`, so the copy carries the *same* index as the authored prefab: a saved city records
the stock prefab and reloads pointing at it. The scope is rebuilt after each load. No serialized
component was added, so no version field was needed.

**UI lookups stay safe for the same reason.** `PrefabSystem.GetPrefab`/`GetPrefabName`
(`Game.Prefabs/PrefabSystem.cs:643-687`, `:979`) resolve purely through `PrefabData.m_Index` into
`m_Prefabs`, never through an entity-keyed dictionary, so a scoped company still resolves to its
original `PrefabBase` and cannot throw.

**Scopes must be dropped, not restored, on load.** `OnGamePreload` calls `Forget()` rather than
`ReleaseAll`, because the recorded entities belong to the outgoing city and their indices can be
reused by new entities. `ReleaseAll` is for `OnStopRunning` and `OnDestroy`, where the world is still
the one the scopes were taken in.

## Signature tenants emptied their own storage on load

**Symptom.** After restarting the game, already-placed signature buildings lost their stock and went
bankrupt. Replacing the building fixed it.

**Cause.** `ProcessingCompanySystem` computes how much to produce from the prefab's storage limit and
never clamps the result at zero (`Game.Simulation/ProcessingCompanySystem.cs:220-224`):

```csharp
int x = storageLimitData.m_Limit - num8;   // limit minus storage already used
num = math.min(x, num);
resources2 = EconomyUtils.AddResources(output.m_Resource, num, resources);
```

A company holding more than its prefab's limit gets a negative `num` and `AddResources` **removes**
that much output. One update is enough to strip the stock; worth then falls below the bankruptcy
limit and the tenant leaves.

Saves restore `PrefabRef` to the authored prefab, whose vanilla limit is far below the mod's, so
every signature tenant stocked above it was over its limit the moment the city loaded. The scope was
only re-established from `OnUpdate`, behind a 64-frame interval — which is exactly the mismatch
window the skill warns about. Replacing the building masked it because a fresh tenant starts empty.

**A second, worse effect on the same cause.** `IndustrialAISystem.cs:144` decrements
`WorkProvider.m_MaxWorkers` whenever stored resources reach **half** the prefab limit:

```csharp
flag4 = value.m_MaxWorkers > kMinimumEmployee && resources >= storageLimitData.m_Limit / 2 && num < 0;
```

So an over-limit tenant loses employees on every AI tick as well as stock, which is why the affected
buildings showed 0% efficiency and no income rather than merely an empty warehouse.

**Fix, and the trap inside the fix.** The scoping pass runs from `OnGameLoaded` and
`OnGameLoadingComplete` so scoped prefabs exist before the first simulation update. `OnGamePreload`
still only calls `Forget()`, since those entities belong to the outgoing world.

The first attempt still failed, because the pass keyed on the building's `Renter` buffer — and
**`Renter` is not serialized**. `Game.Serialization/RenterSystem.cs` rebuilds it during
deserialization from each company's `PropertyRenter`. A load-time pass keyed on `Renter` therefore
runs before the buffers exist, finds every signature building but zero tenants, and does nothing at
all — indistinguishable from success. The load pass now walks tenants via a `PropertyRenter` query,
which is serialized, and resolves the building through `PropertyRenter.m_Property`.

Note also that `GameSystemBase.GameLoadingComplete` catches exceptions and only writes them to the
game log without disabling the system, so a throwing load pass is silent too. The pass now logs its
building, tenant and scope counts at Info on every load, and reports its own failures.

**The actual cause: a scoped copy cannot be referenced from a save.** This was the real defect, and it
took three wrong fixes to find because the damage is written at *save* time while the symptom appears
at *load* time.

`PrefabRef.Serialize` writes the raw entity (`Game.Prefabs/PrefabRef.cs:12`), and entity fields go
through `BinaryWriter.Write(Entity)`
(`Colossal.Core/Colossal.Serialization.Entities/BinaryWriter.cs:128-141`):

```csharp
if (value.Index >= 0 && value.Index < m_EntityTable.Length) {
    Entity entity = m_EntityTable[value.Index];
    if (entity.Version == value.Version) { Write(entity.Index); return; }
}
Write(-1);
```

`m_EntityTable` covers the entities actually being serialized. A scoped copy carries
`Unity.Entities.Prefab`, which is exactly what keeps it out of every query — including the ones that
build that table. So a tenant saved while pointing at a copy has its `PrefabRef` written as `-1`, and
`BinaryReader.Read(out Entity)` maps `-1` back to `Entity.Null`
(`BinaryReader.cs:146-157`). The company reloads with no prefab at all.

The earlier claim in this file that `PrefabRef` serializes via `PrefabData.m_Index` was **wrong**.
`PrefabReferences.Check` marks which prefabs belong in the save's prefab table; it does not encode the
field. The property that makes the copy invisible to the simulation is the same property that makes
it unserializable, and that tension is inherent to the approach.

**Fix.** `SignatureFixSystem` implements `Game.Serialization.IPreSerialize` and releases every scope
before the write; `SignatureScopeRestoreSystem` re-applies them after `WriteSystem`. Both are
registered in `SystemUpdatePhase.Serialize`, so the release and restore are separated by no simulation
frame at all — a tenant is never exposed to the vanilla limit, which is what would otherwise let
`ProcessingCompanySystem` and `IndustrialAISystem` strip it.

**Cities saved by 1.0.8 cannot be repaired by the mod.** Their tenants hold `Entity.Null` for a
prefab, and the identity needed to pick the right one died with the reference. `Apply` logs a warning
and leaves them alone; replacing the building reseats a healthy tenant.

**The copy lifecycle could also strand a company with no prefab at all.** The panel of an affected
building is the tell: every row sourced from the *company* entity survived (Company, Income, Costs,
Profit, Bank Balance) while every row sourced from the *prefab* vanished (Type, Requires, Produces,
Production, Employees, Storage). That is not an over-limit company — it is a company whose
`PrefabRef` no longer resolves.

`Release` caused it. It destroyed the copy unconditionally, but only restored `PrefabRef` when the
recorded `m_Source` still existed:

```csharp
if (... && entityManager.Exists(scoped.m_Source))
    entityManager.SetComponentData(company, new PrefabRef { m_Prefab = scoped.m_Source });
if (entityManager.Exists(scoped.m_Clone))
    entityManager.DestroyEntity(scoped.m_Clone);   // ran either way
```

Prefab entities are not stable across a load: `PrefabSystem` rebuilds them and has
`ReplacePrefabSystem` swap the old entity out (`Game.Prefabs/PrefabSystem.cs:798-805`). Once the
recorded source had been replaced, the restore was skipped and the copy destroyed anyway, leaving
the company pointing at a destroyed entity — no process, no workplaces, no storage limit, zero
efficiency, and unrecoverable short of replacing the building.

`Release` now removes the copy only when the company no longer references it, recovers the current
authored prefab through `PrefabData.m_Index` when the recorded source has been replaced, and leaks
the copy rather than stranding the company when neither is possible. A leaked copy is harmless — it
carries `Unity.Entities.Prefab`, so no query sees it.

**`OnGameLoaded` is the wrong hook.** It fires from `LoadGameSystem.onOnSaveGameLoaded`, mid-pipeline,
before prefab references are resolved back into entities and while `PrefabSystem` may still be
replacing prefab entities. Cloning from a `PrefabRef` read there copies an unresolved or
soon-to-be-stale entity. Only `OnGameLoadingComplete` is used.

**Ordering is not sufficient on its own.** `SignatureFixSystem` is already ordered before
`ResourceBuyerSystem` (`Game.Common/SystemOrder.cs:394`), which puts it ahead of
`ProcessingCompanySystem` (`:454`), `IndustrialAISystem` (`:495`) and `CompanyMoveAwaySystem`
(`:505`) within a frame. But `GetUpdateInterval` is 64, so those systems can still run on frames
where this one does not. Only the load hooks close the window.

**Related, not yet handled.** The same arithmetic applies when the player *lowers* the storage
slider below what a company currently holds: the next production update destroys the excess rather
than letting it drain. Worth deciding whether to ramp reductions or warn in the UI.

## Raising the worker ceiling is not the same as raising the workers

The worker multiplier scales `IndustrialProcessData.m_MaxWorkersPerCell`, which
`CompanyUtils.GetIndustrialAndOfficeFittingWorkers` (`Game.Simulation/CompanyUtils.cs:42`) turns into
the employment ceiling. That part worked immediately — and looked completely broken, because the
ceiling is not what staffs the building.

`IndustrialAISystem` walks `WorkProvider.m_MaxWorkers` toward the ceiling **one worker per update**,
and only when the company is already fully staffed and holding less than a quarter of its storage
limit (`:145`, `:171-173`):

```csharp
flag5 = length == value.m_MaxWorkers && industrialAndOfficeFittingWorkers - value.m_MaxWorkers > 1
        && resources <= storageLimitData.m_Limit / 4;
else if (flag5) value.m_MaxWorkers++;
value.m_MaxWorkers = math.clamp(value.m_MaxWorkers, kMinimumEmployee, industrialAndOfficeFittingWorkers);
```

42 to 420 workers is several hundred qualifying updates. The production multiplier appeared to work
instantly by comparison only because the recipe is re-read every tick.

`SignatureFixSystem.RaiseWorkerCeiling` therefore sets `m_MaxWorkers` directly to the scaled fitting
count. It only ever raises: shedding staff is the game's decision, and the `math.clamp` on the last
line brings the value back down on its own when the multiplier is lowered.

**Commercial tenants use a different field.** `GetCommercialMaxFittingWorkers` (`:37`) reads
`ServiceCompanyData.m_MaxWorkersPerCell`, not the process, so scaling only `IndustrialProcessData`
left commercial signature buildings completely unaffected by the worker slider. Both are scaled now.

**Write it once, not every pass.** The first version called `RaiseWorkerCeiling` on every update, so
it re-asserted `m_MaxWorkers` every 64 frames. That is indistinguishable from a fight with any other
mod that manages workplaces, and it broke one:
[Change Company](https://github.com/rcav8tr/CS2Mod-ChangeCompany) has a **Company Workplaces**
feature that overrides workplace count — explicitly including signature buildings — and documents
that "a company workplaces override prevents this normal game logic". A player's override was
overwritten within 64 frames and forced up to this mod's scaled ceiling, so the count skyrocketed and
could not be changed.

`Apply` now reports whether the worker multiplier just changed, and the ceiling is nudged only on
that pass, and only when the multiplier is above 1x. At 1x this mod does not touch `m_MaxWorkers` at
all. Afterwards the value is left alone, so another mod's override — or the game's own adjustment —
sticks.

The general rule this is an instance of: a periodic system that re-asserts a value it does not own
will silently defeat every other mod touching that value.

**"Only on transition" was the wrong correction, though.** It broke the slider. The scope is created
in `ApplyScopedLimits`, which runs on load *and* after every save — `PreSerialize` releases every
scope and `SignatureScopeRestoreSystem` rebuilds it — and that path called the `Apply` overload that
discarded the change flag. So the one pass that would have written the ceiling never did, and the
following `OnUpdate` saw a request that already matched. The prefab copy carried the scaled ceiling
correctly; only `m_MaxWorkers` was left creeping at +1 per update.

**Reading the other mod settled it.** `Systems/CompanyWorkplacesSystem.cs` in
[Change Company](https://github.com/rcav8tr/CS2Mod-ChangeCompany) Harmony-postfixes the OnUpdate of
`CommercialAISystem`, `ExtractorAISystem` and `IndustrialAISystem`, then runs
`OverrideWorkplacesJob` and calls `Dependency.Complete()` so nothing can observe the AI's value first.
Decisively, its query **requires** the mod's own `WorkplacesOverride` component:

```csharp
_companyQuery = GetEntityQuery(
    ComponentType.ReadOnly<WorkplacesOverride>(),
    ComponentType.ReadWrite<WorkProvider>(), ...
```

So it only touches companies the player has explicitly given an override, and never any other. There
was no blanket conflict to solve — testing for the same component is an exact match for its scope,
and is the same coexistence its readme describes for Realistic Workplaces and Households.
`Mod.WorkplacesOverrideType` resolves that type by name from the loaded assemblies, and
`ApplyWorkerCeiling` skips any company carrying it.

**The reported 116 to 231 was this mod's own bug, not the other mod.** No override was set, so Change
Company was not involved at all.

**In the end, value-equality ownership was the wrong mechanism.** Remembering the exact value written and yielding whenever the live
number differed looks reasonable and fails immediately: the company AI moves `m_MaxWorkers` by one
every tick, so the claim was lost within a tick or two. A 10x setting stalled near 2x — the first
value written while the slider was dragged past it, minus the AI's decrement. 232 written, 231
observed.

The rule that works is scope, not equality: skip companies the other mod has been told to manage, and
manage the rest unconditionally. A **Use Change Company for employees** option remains for anyone who
wants this mod out of workplaces entirely, shown only when Change Company is detected and off by
default, gated through `Mod.DeferWorkersToChangeCompany` and applied in `ResolveBuildingScope` alone.
Detection failure is non-fatal: if the mod list cannot be read it behaves as if absent.

## Random NullReferenceException from Colossal.Logging (issue #8)

**Symptom.** An error dialog appearing at random, with the exception thrown inside
`Colossal.Logging.UnityLogger.Internal_WriteStream` and the mod frame pointing at an ordinary
`Mod.log.Info(...)` call in `SignatureFixSystem.OnUpdate`. The reporter correctly noted there was
nothing wrong with the values being formatted.

**Cause is in the logger, not the arguments.** With `keepStreamOpen` false — the default from
`LogManager.GetLogger` — the write path closes the file after every message:

- `Colossal.Logging/UnityLogger.cs:343-346` — `Close()` after each write, nulling `m_Stream` and
  `m_StreamWriter`.
- `:312-315` — the next write sees `isOpen == false` and calls `Open()`.
- `:372-386` — `Open()` wraps the `FileStream` construction in `try { } catch { Close(); }`. A bare
  catch. A transient lock on the file (antivirus, a log tailer, cloud sync, the indexer) leaves
  `m_StreamWriter` null and raises nothing.
- `:318` — `Internal_WriteStream` then calls `m_StreamWriter.SetSourceAndStd(...)` with no null
  check. The reported IL offset `0x0001f` matches this first dereference.
- `:348` — the enclosing try catches only `IOException`, so the NRE escapes into Unity's log handler,
  which reports it as an exception dialog.

`SetShowsErrorsInUI(false)` cannot suppress it, because the exception is thrown *inside* the logging
machinery rather than reported through the logger.

**Why it looked random and why it pointed at that line.** The mod logs from `OnUpdate` on a 64-frame
interval, and every one of those calls reopened the file. The cited line was simply the most frequent
write in the hot path, so it drew the odds.

**Fix.** `Mod.CreateLogger` sets `keepStreamOpen = true`, removing the reopen entirely, and the
per-update counters were demoted from Info to Debug so a busy city is not writing several times a
second in the first place.

## Pre-existing hazards fixed alongside

- The renter loop iterated a live `DynamicBuffer<Renter>` while making structural changes
  (`AddComponentData`, `RemoveComponent`). The buffer is now copied to a temporary `NativeArray`
  first.
- `ComponentLookup`/`BufferLookup` handles were captured once before the loop and used after
  structural changes. They are now refreshed with `Update(this)` on each iteration.
- The building array from the query is a snapshot, so a building can be destroyed mid-loop. Existence
  is now checked before use.

## Not yet measured

The scoping change is verified by reading the decompiled consumers, not by observation in a running
city. Worth confirming in game:

1. Place a signature building, then check that an unrelated zoned company of the same type still
   shows vanilla vehicle and storage caps.
2. Bulldoze the signature building and confirm the tenant's copy is released (log line reports the
   active scope count).
3. Save and reload, and confirm the scope is re-established and no stale prefab is referenced.

Players already affected do not need to do anything: prefabs are rebuilt from assets on every load,
so the old ratcheted values disappear on restart.
