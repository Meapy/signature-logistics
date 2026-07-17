# Fix Signatures

A Cities: Skylines II code mod that lets you choose the vehicle and storage limits for signature buildings and keep their production inputs stocked.

## Usage

Open **Options > Fix Signatures** and configure:

- **Maximum vehicles**: 1–100, default 10.
- **Maximum storage (tonnes)**: 10–5,000 t, default 300 t.
- **Input restock target**: 25–100%, default 75%.

Changes apply during gameplay to existing signature buildings and to buildings placed later.

When a required production input plus deliveries already on the way falls below the restock target, the mod asks the game's normal purchase system for another truckload. The game still uses real local suppliers or outside connections, pays normal costs, and requires a working delivery route.

Only signature-building companies are changed. Service vehicle capacities, ordinary zoned companies, and cargo stations are left untouched.

## Build

Install and initialize the Cities: Skylines II modding toolchain in-game, then build `Fix-Signatures.slnx` with Visual Studio or `dotnet build`.

The project also accepts the toolchain path as an MSBuild override:

```powershell
dotnet build Fix-Signatures.slnx --configfile NuGet.Config -p:CSIIToolPath="C:\path\to\.ModdingToolchain"
```
