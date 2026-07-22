# Paradox forum post

Live thread: https://forum.paradoxplaza.com/forum/threads/mod-signature-logistics-configurable-vehicles-storage-imports-and-company-stability.1935899/

## Title

[MOD] Signature Logistics – configurable vehicles, storage, imports, and company stability

## Body

Signature Logistics is a Cities: Skylines II code/UI mod for signature factories that spend too much time idle because of small vehicle fleets, limited storage, missing production inputs, or frequent company replacement.

Current version: 1.0.6  
Game compatibility: 1.6.0*  
Paradox Mods: https://mods.paradoxplaza.com/mods/151747/Windows  
Source and issue tracker: https://github.com/Meapy/signature-logistics

WHAT IT DOES

- Configurable global vehicle limit (1–100, default 20).
- Configurable global storage limit (10–5,000 tonnes, default 500 tonnes).
- Configurable input restock target (25–100%, default 25%).
- Saved vehicle and storage overrides for individual signature factories.
- Recipe-aware restocking that prioritizes the input closest to stopping production.
- Full-load priority imports using normal costs, vehicles, routes, and game pathfinding.
- Protection from the game's random company churn while preserving genuine bankruptcy.
- A one-time second copy of each new signature tenant's non-money starting resources.
- A saved “Previous company left” reason under the Company section.
- Cargo, capacity, and approximate destination distance in Vehicles in use.

HOW TO USE IT

Set global defaults under Options > Signature Logistics. Select a signature factory to edit its Building logistics vehicle and storage limits; Use global removes that building's override. Global settings persist across restarts and building overrides save with the city.

SCOPE AND COMPATIBILITY

Only signature-building companies are changed. Ordinary zoned companies, service vehicles, cargo stations, and unrelated buildings retain vanilla behavior. The mod does not teleport or create purchased resources: priority imports still pay normal costs, dispatch real vehicles, follow the native pathfinder, and require working road connections.

FEEDBACK AND BUG REPORTS

Please reply here with feedback. For a bug report, include:

1. The Signature Logistics version and current Cities: Skylines II version.
2. The affected signature building and its global/building logistics values.
3. What happened and what you expected.
4. Whether it still happens after restarting the game.
5. A screenshot and relevant Player.log/UI.log excerpt if available.

You can also report reproducible problems on GitHub: https://github.com/Meapy/signature-logistics/issues
