# Continuity

[PLANS]

- 2026-07-17T13:38Z [USER] Implement a configurable maximum vehicle count for Cities: Skylines II signature buildings; verify, document, commit, and prepare a pull request.
- 2026-07-17T14:53Z [USER] Keep signature manufacturing buildings supplied more aggressively so production does not routinely stop for missing inputs.
- 2026-07-17T15:02Z [USER] Review and improve pathfinding-related efficiency and performance.
- 2026-07-17T15:09Z [USER] Analyze the installed game's actual pathfinding/buyer code and apply further safe optimizations in the mod.
- 2026-07-17T15:20Z [USER] Persist all slider changes across game restarts.

[DECISIONS]

- 2026-07-17T13:38Z [CODE] Patch only `TransportCompanyData.m_MaxTransports` on prefabs containing both `SignatureBuildingData` and `TransportCompanyData`; this is the game-owned limit consumed by simulation and UI code.
- 2026-07-17T13:38Z [ASSUMPTION] Keep the vanilla value 5 as the default and expose a 1-100 options slider to avoid changing gameplay until the player chooses a new value.
- 2026-07-17T14:15Z [CODE] Run `SignatureFixSystem` in `GameSimulation`, after prefab component initialization, while retaining the value-change guard so the per-frame phase adds no repeated writes.
- 2026-07-17T14:28Z [USER] Supersedes the default-value decision above: the mod default is 10; the persisted 1-100 slider remains authoritative at runtime.
- 2026-07-17T14:28Z [CODE] Follow placed `Game.Buildings.Signature` instances through their `Renter` company and `PrefabRef`, then update that company prefab's `TransportCompanyData` only when its value differs.
- 2026-07-17T14:41Z [USER] Add configurable signature-building storage with a default of 300 tonnes.
- 2026-07-17T14:41Z [CODE] Store the setting in tonnes (10-5,000, step 10) and convert to game resource units at 1,000 units per tonne before updating company-prefab `StorageLimitData.m_Limit`.
- 2026-07-17T14:53Z [CODE] Use the game's native `ResourceBuyer` route with local-industrial and import targets; default the configurable input target to 75% of each resource's storage share and count pending trips plus cargo already inbound before ordering.
- 2026-07-17T15:02Z [CODE] Match `SignatureFixSystem` to the native `ResourceBuyerSystem` 16-tick update interval, preserving purchase responsiveness while eliminating per-frame polling.
- 2026-07-17T15:09Z [CODE] Supersedes the 16-tick decision above: run priority restocking every 64 ticks, still 4x more often than vanilla company buying, to reduce both steady-state scans and repeated failed path searches without adding per-company retry state.
- 2026-07-17T15:20Z [CODE] Rename the generic `Setting` type to unique `SignatureFixSettings` while preserving `[FileLocation("SignatureFix")]` and property names, avoiding cross-mod save lookup collisions without migrating the existing file.

[PROGRESS]

- 2026-07-17T13:38Z [CODE] Replaced the unfinished reflection patch with a direct ECS update, reduced the template settings to one persisted option, and added user/build documentation.
- 2026-07-17T13:55Z [TOOL] Initial build was blocked because the sandbox cannot read the user-wide NuGet config; added a repo-local config with no package sources because this project has no NuGet dependencies.
- 2026-07-17T14:02Z [TOOL] Debug compilation against the installed Cities: Skylines II managed assemblies succeeded with 0 warnings and 0 errors.
- 2026-07-17T14:53Z [CODE] Added per-company lowest-input selection, one-truck-at-a-time priority purchasing, a 25-100% restock slider, and usage documentation.
- 2026-07-17T15:02Z [CODE] Reduced signature scans and their resource/trip/vehicle-buffer reads from every tick to once per 16 ticks.
- 2026-07-17T15:09Z [CODE] Reduced scans again from every 16 to every 64 ticks and added a current-stock early return before pending-trip and owned-vehicle buffer walks.
- 2026-07-17T15:20Z [CODE] Updated all settings references and documentation; automatic native slider `ApplyAndSave` now resolves the unique settings type.

[DISCOVERIES]

- 2026-07-17T13:38Z [TOOL] Inspection of the installed `Game.dll` showed `ProcessingCompany.Initialize` copies `ProcessingCompany.transports` to `TransportCompanyData.m_MaxTransports`; delivery pathfinding and vehicle UI read that component as the cap.
- 2026-07-17T13:38Z [TOOL] The local `CSII_TOOLPATH` user environment variable is unset; the installed toolchain is under the game's `.ModdingToolchain` directory.
- 2026-07-17T14:15Z [TOOL] The game loaded the original DLL but emitted no patch-count log; `PrefabUpdate` ran before `ProcessingCompany.Initialize` materialized `TransportCompanyData`, so `RequireForUpdate` prevented the system from running.
- 2026-07-17T14:28Z [TOOL] A second restart still produced no patch-count log, proving signature markers and transport caps are on different prefab entities; game IL shows the UI resolves `building -> Renter company -> company PrefabRef -> TransportCompanyData`.
- 2026-07-17T14:41Z [TOOL] Game IL shows `StorageSection.Visible` reads `StorageLimitData.m_Limit` from the same renter-company prefab and treats resource units as weight; the displayed 100 t corresponds to 100,000 internal units.
- 2026-07-17T14:53Z [TOOL] Game IL shows vanilla company buying waits until an input falls below 25% of its storage share and normally issues one request at a time; `ResourceBuyerSystem` accepts `Industrial | Import`, creates a normal paid shopping trip, and imports through an outside connection when appropriate.
- 2026-07-17T15:02Z [TOOL] `ResourceBuyerSystem.GetUpdateInterval` returns 16 while `BuyingCompanySystem` returns 256; the mod's previous default interval was 1, causing 16 scans per native purchase-processing opportunity.
- 2026-07-17T15:09Z [TOOL] Installed `Game.dll` shows `ResourceBuyerSystem.HandleBuyersJob` is Burst-compiled and scheduled with `ScheduleParallel`; it enqueues native path searches and removes failed company requests without company-level retry backoff, so an aggressive mod can otherwise resubmit failures every buyer cycle.
- 2026-07-17T15:20Z [TOOL] Installed game IL shows every int slider calls `Setting.ApplyAndSave`, which calls `SaveSpecificSetting(GetType().Name)`; AssetDatabase then matches only `fragment.source.GetType().Name`, so generic mod classes named `Setting` collide. Existing `SignatureFix.coc` was last written at 2026-07-17T14:14:28Z and contained only `MaxVehicles: 20`.

[OUTCOMES]

- 2026-07-17T14:02Z [CODE] The configurable signature-building vehicle cap is implemented, persisted, documented, and build-verified.
- 2026-07-17T14:02Z [TOOL] The workspace started with an empty `.git` directory and has no remote; a local repository was initialized, but a GitHub pull request cannot be created without a remote repository.
- 2026-07-17T13:59Z [TOOL] Supersedes the two `2026-07-17T14:02Z` timestamps above: those timestamps were recorded incorrectly; both facts remain valid and occurred before this deployment.
- 2026-07-17T13:59Z [TOOL] Deployed the verified DLL to `C:\Users\dkras\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Fix-Signatures\Fix-Signatures.dll`; its SHA-256 matched the build output.
- 2026-07-17T14:15Z [TOOL] Rebuilt the timing fix with 0 warnings/errors and redeployed a hash-matching DLL; an in-game restart/retest remains pending.
- 2026-07-17T14:28Z [TOOL] Built the instance-to-company fix and default 10 with 0 warnings/errors, then redeployed a hash-matching 9,728-byte DLL; restart/retest remains pending.
- 2026-07-17T14:41Z [TOOL] Built configurable storage with 0 warnings/errors and deployed a hash-matching 10,240-byte DLL; restart/retest remains pending.
- 2026-07-17T14:53Z [TOOL] The priority-restocking implementation builds successfully with 0 warnings and 0 errors; deployed the 13,824-byte DLL and verified source/destination SHA-256 `DB1897F71B2B747AC01C0C08C293FAD7712E9764E94EA267B13ACA429D8F2CB8` match. In-game restart/retest remains pending.
- 2026-07-17T15:02Z [TOOL] The 16-tick scheduling optimization builds with 0 warnings/errors; built IL confirms the override returns 16. Deployed the 13,824-byte DLL and verified SHA-256 `741A93525AC908D287616E3FB4DC8ECB490207A0B0417177D7E650D400F2AE27`; restart/retest remains pending.
- 2026-07-17T15:09Z [TOOL] The deeper optimization builds with 0 warnings/errors; built IL confirms a 64-tick interval and that current-stock comparison returns before trip-buffer access. Deployed the 13,824-byte DLL and verified SHA-256 `AA772DF412E3252CF542B6A26B42A24D77CE086BE7120D5C0D5B5EBE986031ED`; restart/in-game profiling remains pending.
- 2026-07-17T15:20Z [TOOL] Persistence fix builds with 0 warnings/errors; compiled metadata confirms unique `SignatureFix.SignatureFixSettings`, no legacy generic type, file location `SignatureFix`, and all three persisted properties. Deployed the 13,824-byte DLL and verified SHA-256 `CC30084FAFC713AB7901CF86AF45EB2FFD3C3D1D2652228645BECC1F19FD0834`; restart/save verification remains pending.
