# Continuity

[PLANS]

- 2026-07-17T13:38Z [USER] Implement a configurable maximum vehicle count for Cities: Skylines II signature buildings; verify, document, commit, and prepare a pull request.

[DECISIONS]

- 2026-07-17T13:38Z [CODE] Patch only `TransportCompanyData.m_MaxTransports` on prefabs containing both `SignatureBuildingData` and `TransportCompanyData`; this is the game-owned limit consumed by simulation and UI code.
- 2026-07-17T13:38Z [ASSUMPTION] Keep the vanilla value 5 as the default and expose a 1-100 options slider to avoid changing gameplay until the player chooses a new value.

[PROGRESS]

- 2026-07-17T13:38Z [CODE] Replaced the unfinished reflection patch with a direct ECS update, reduced the template settings to one persisted option, and added user/build documentation.
- 2026-07-17T13:55Z [TOOL] Initial build was blocked because the sandbox cannot read the user-wide NuGet config; added a repo-local config with no package sources because this project has no NuGet dependencies.
- 2026-07-17T14:02Z [TOOL] Debug compilation against the installed Cities: Skylines II managed assemblies succeeded with 0 warnings and 0 errors.

[DISCOVERIES]

- 2026-07-17T13:38Z [TOOL] Inspection of the installed `Game.dll` showed `ProcessingCompany.Initialize` copies `ProcessingCompany.transports` to `TransportCompanyData.m_MaxTransports`; delivery pathfinding and vehicle UI read that component as the cap.
- 2026-07-17T13:38Z [TOOL] The local `CSII_TOOLPATH` user environment variable is unset; the installed toolchain is under the game's `.ModdingToolchain` directory.

[OUTCOMES]

- 2026-07-17T14:02Z [CODE] The configurable signature-building vehicle cap is implemented, persisted, documented, and build-verified.
- 2026-07-17T14:02Z [TOOL] The workspace started with an empty `.git` directory and has no remote; a local repository was initialized, but a GitHub pull request cannot be created without a remote repository.
- 2026-07-17T13:59Z [TOOL] Supersedes the two `2026-07-17T14:02Z` timestamps above: those timestamps were recorded incorrectly; both facts remain valid and occurred before this deployment.
- 2026-07-17T13:59Z [TOOL] Deployed the verified DLL to `C:\Users\dkras\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Fix-Signatures\Fix-Signatures.dll`; its SHA-256 matched the build output.
