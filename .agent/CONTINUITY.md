# Continuity

[PLANS]

- 2026-07-17T13:38Z [USER] Implement a configurable maximum vehicle count for Cities: Skylines II signature buildings; verify, document, commit, and prepare a pull request.
- 2026-07-17T14:53Z [USER] Keep signature manufacturing buildings supplied more aggressively so production does not routinely stop for missing inputs.
- 2026-07-17T15:02Z [USER] Review and improve pathfinding-related efficiency and performance.
- 2026-07-17T15:09Z [USER] Analyze the installed game's actual pathfinding/buyer code and apply further safe optimizations in the mod.
- 2026-07-17T15:20Z [USER] Persist all slider changes across game restarts.
- 2026-07-17T16:10Z [USER] Show each active delivery vehicle's cargo and distance to its destination on the existing Vehicles in use row.
- 2026-07-17T16:34Z [USER] Make the working vehicle-detail rows clearer and more intuitive after the original extension wrapped status and details onto two lines.
- 2026-07-18T14:04Z [USER] Correct the revised three-column layout, which crushed vehicle names and clipped state/distance at the panel edge.
- 2026-07-18T14:31Z [USER] Add saved per-building maximum vehicle and storage overrides, edited from the selected signature factory, with global sliders as fallbacks.
- 2026-07-18T15:43Z [USER] Prepare the mod for Paradox Mods publishing, rename it to something more applicable than Fix Signatures, and replace the AI-looking store artwork.
- 2026-07-18T15:49Z [USER] Publish Signature Logistics to Paradox Mods.

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
- 2026-07-17T16:10Z [CODE] Preserve the native `VehicleItem` row and state link, append localized cargo/capacity and distance through the official UI module registry, and publish selected-company details through a throttled managed binding.
- 2026-07-17T16:10Z [CODE] Report straight-line world distance to the current `VehicleUIUtils` destination; exact road-route distance is not maintained by the game as a decrementing UI value and would require walking live path/lane geometry.
- 2026-07-18T14:31Z [CODE] Save two validated integers on each selected signature building through an `ISerializable` ECS component; the simulation uses them before global defaults, and removing the component resets the building to global values.
- 2026-07-18T15:43Z [CODE] Use `Signature Logistics` for every player-facing/store name while retaining internal `SignatureFix` binding, setting, namespace, and assembly identifiers so existing settings and serialized city components remain compatible.
- 2026-07-18T15:43Z [CODE] Package the already-tested UI bundle through native MSBuild content items and fail Release builds when it is absent; do not add a second packaging system.
- 2026-07-18T15:49Z [CODE] Store returned Paradox Mods ID `151747` in publish metadata and use only the server-valid `Code Mod` tag for future releases.

[PROGRESS]

- 2026-07-17T13:38Z [CODE] Replaced the unfinished reflection patch with a direct ECS update, reduced the template settings to one persisted option, and added user/build documentation.
- 2026-07-17T13:55Z [TOOL] Initial build was blocked because the sandbox cannot read the user-wide NuGet config; added a repo-local config with no package sources because this project has no NuGet dependencies.
- 2026-07-17T14:02Z [TOOL] Debug compilation against the installed Cities: Skylines II managed assemblies succeeded with 0 warnings and 0 errors.
- 2026-07-17T14:53Z [CODE] Added per-company lowest-input selection, one-truck-at-a-time priority purchasing, a 25-100% restock slider, and usage documentation.
- 2026-07-17T15:02Z [CODE] Reduced signature scans and their resource/trip/vehicle-buffer reads from every tick to once per 16 ticks.
- 2026-07-17T15:09Z [CODE] Reduced scans again from every 16 to every 64 ticks and added a current-stock early return before pending-trip and owned-vehicle buffer walks.
- 2026-07-17T15:20Z [CODE] Updated all settings references and documentation; automatic native slider `ApplyAndSave` now resolves the unique settings type.
- 2026-07-17T16:10Z [CODE] Added `VehicleDetailsUISystem`, a minimal JS/CSS vehicle-row extension, pinned UI build tooling, a built-bundle smoke test, local container recipe, and updated usage/publishing documentation.
- 2026-07-18T14:31Z [CODE] Added selected-building limit bindings/triggers, compact native sliders below the vehicle section, saved override/reset handling, and per-building restock/storage/vehicle application.
- 2026-07-18T15:43Z [CODE] Completed initial-store metadata, valid Code Mod/UI tags, compatibility/version data, release instructions, UI-inclusive Release packaging, and new Signature Logistics thumbnail artwork.
- 2026-07-18T15:49Z [TOOL] Published the verified Release package through the official installed ModPublisher and recorded its returned ID for subsequent `NewVersion` uploads.

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
- 2026-07-17T16:10Z [TOOL] Installed game code shows `VehiclesSection` serializes only entity/name/type/state; its frontend `VehicleItem` is an overridable module-registry export, so extra fields require a mod binding plus UI override rather than a hidden native option.
- 2026-07-17T16:10Z [TOOL] Native cargo UI sums loaded `DeliveryTruck.m_Amount` across `LayoutElement` vehicles and capacity comes from each prefab's `DeliveryTruckData.m_CargoCapacity`; the mod mirrors that behavior and uses the game's localized Weight and Length formatters.
- 2026-07-17T16:24Z [TOOL] In-game retest showed native vehicle rows unchanged; `UI.log` had no Fix Signatures registration. Installed working packages contained 36 `.mjs` modules and no `.js` modules, while `DefaultAssetFactory.RegisterSupportedTypes` explicitly registers `.mjs`, proving the original `.js` bundle was never discovered.
- 2026-07-18T14:04Z [TOOL] In-game screenshot showed the native `InfoRow` three-column sizing plus `noShrinkRight` compressed names to near-unreadable text and overflowed the right link; reusing its original two-column layout avoids both constraints.
- 2026-07-18T14:31Z [TOOL] Installed `Game.dll` shows vehicle capacity and production/storage simulation read `TransportCompanyData` and `StorageLimitData` from the renter company's prefab rather than instance components; signature factories are unique, so the selected building's saved values are applied to its dedicated company prefab.
- 2026-07-18T14:31Z [TOOL] Installed mods confirm custom `IComponentData` plus `Colossal.Serialization.Entities.ISerializable` is the established city-save persistence pattern; compiled metadata confirms the new component implements both interfaces.
- 2026-07-18T15:43Z [TOOL] Current ModPublisher validation requires non-empty display/description/thumbnail/version/game-version fields and at least one tag; the template's empty tag and placeholder metadata were not publish-ready.
- 2026-07-18T15:43Z [TOOL] The installed game reports `1.6.0f1`, and a locally cached published code-mod configuration confirms `Code Mod`, `UI`, and `1.6.0*` as current metadata values.
- 2026-07-18T15:43Z [TOOL] The original Release deploy path contained only managed/native artifacts; adding the two UI files as MSBuild content makes both Release output and publisher deploy folder complete.
- 2026-07-18T15:49Z [TOOL] Supersedes the cached-metadata portion of the 15:43 discovery above: the live Paradox API rejects tag `UI`; `Code Mod` remains valid, and removing only `UI` allowed publication.

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
- 2026-07-17T16:10Z [TOOL] Vehicle-detail feature builds with 0 C# warnings/errors; the UI webpack build and bundle-level row-extension smoke test pass. Deployed matching `Fix-Signatures.dll` (SHA-256 `9F8A1A5B2480F87F4E0896C5C9F3F82432611525DDACE53CDE06612C2772A9A3`), JS (`375E87D03894B3076FB14DB7CE8F1160DD62D20A5F905DF2CE4BB1C405BD8B29`), and CSS (`4C97F848A7F1A21347DC72E11F2ADF9FDB9E4C6F2A50DB5DBFD16CC7589BC9DB`) to the local game mod folder; restart/in-game visual verification remains pending.
- 2026-07-17T16:24Z [TOOL] Corrected the UI artifact extension to `.mjs`, added a UI registration log marker, reran the built-bundle smoke test successfully, deployed hash-matching SHA-256 `075CCEBB6340DA87601F3A0AF76CBA920525F4A8E6C60BDC0B5231A5F41BB7A6`, and removed the obsolete `.js`; full game restart/retest remains pending.
- 2026-07-17T16:34Z [TOOL] Reworked the native row into three columns: vehicle name, centered resource cargo/capacity, and clickable state plus distance. UI build/smoke test pass; deployed hash-matching MJS `5960F9985ADBC4B71E1DA7797E67C65A2FFBD6A189B44CA9767D93053119E937` and CSS `183A31DE2034A32E6597B7600FCFED69B78693F19D34A7525C323FD11861AC37`; restart/retest remains pending.
- 2026-07-18T14:04Z [TOOL] Replaced the failing three-column row with the native two-column layout: full-size name plus compact cargo on the left, clickable state plus smaller distance on the right. UI build/smoke test pass; deployed hash-matching MJS `FAE43CA9C8B1C139769F370DEBF81B31E08507EFEFEBDA9923CA6F1FA8863969` and CSS `9418CE8294A5F186F059D115F7879F5A4B1DFA7006A0A0104F7A7A11B89503C0`; restart/retest remains pending.
- 2026-07-18T14:31Z [TOOL] Per-building vehicle/storage overrides build with 0 managed warnings/errors and pass the UI build/interaction smoke test. Deployed hash-matching DLL `A85478922803283936E0F6A622EF2BC0D106D3975BEAA4E7B19715439F35A507`, MJS `E9D1CA2E0084FF131FF0C3D35BF812715339507C36188B9403F68538CD96BE00`, and CSS `43FC07622776B583C91F17B2FB499FFB477CBAAEB2BA272A70FC55A1146FE15F`; restart, slider, reset, and save/reload testing remain pending.
- 2026-07-18T15:43Z [TOOL] `Signature Logistics` metadata and 950x500 thumbnail validate locally; pinned npm UI build/smoke test passes, Docker is unavailable, and the complete Release build succeeds with 0 warnings/errors. Release/deploy hashes match for DLL `0BFD6DC78E660F88510B9C27B912C12F255D6ABA16C68F8F1D97B8CC1554C858`, MJS `939027EFAA230C1F54020915A62BC21B2FFDFED6C11A35B87742E24F4495705D`, and CSS `43FC07622776B583C91F17B2FB499FFB477CBAAEB2BA272A70FC55A1146FE15F`; final in-game per-building/save-reload test and the user-authorized upload remain release gates.
- 2026-07-18T15:49Z [TOOL] Signature Logistics 1.0.0 is publicly published as Paradox Mods ID `151747`; `https://mods.paradoxplaza.com/mods/151747/Windows` returns HTTP 200 with title `Signature Logistics - Paradox Mods`. The upload gate above is complete; in-game save/reload verification remains recommended follow-up testing.
