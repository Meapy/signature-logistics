# Fix Signatures

A Cities: Skylines II code/UI mod that lets you choose the vehicle and storage limits for signature buildings, keep their production inputs stocked, and inspect active deliveries.

## Usage

Open **Options > Fix Signatures** and configure:

- **Maximum vehicles**: 1-100, default 10.
- **Maximum storage (tonnes)**: 10-5,000 t, default 300 t.
- **Input restock target**: 25-100%, default 75%.

Changes save automatically, load on the next game start, and apply during gameplay to existing signature buildings and buildings placed later.

When a required production input plus deliveries already on the way falls below the restock target, the mod asks the game's normal purchase system for another truckload. The game still uses real local suppliers or outside connections, pays normal costs, and requires a working delivery route.

Only signature-building companies are changed. Service vehicle capacities, ordinary zoned companies, and cargo stations are left untouched.

Expand **Vehicles in use** on a building to see each delivery vehicle's current cargo/capacity and approximate straight-line distance to its current destination on the same row. The game's original state link remains clickable.

## Build

Install and initialize the Cities: Skylines II modding toolchain in-game, then build `Fix-Signatures.slnx` with Visual Studio or `dotnet build`.

The project also accepts the toolchain path as an MSBuild override:

```powershell
dotnet build Fix-Signatures.slnx --configfile NuGet.Config -p:CSIIToolPath="C:\path\to\.ModdingToolchain"
```

Build the UI module separately with the official template's pinned tooling:

```powershell
cd Fix-Signatures.UI
npm install
npm run build
```

For a local installation, place `Fix-Signatures.dll`, `Fix-Signatures.mjs`, and `Fix-Signatures.css` together in the game's `Mods\Fix-Signatures` folder. The current game UI loader discovers ES modules by the `.mjs` extension.
