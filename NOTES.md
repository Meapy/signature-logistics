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
