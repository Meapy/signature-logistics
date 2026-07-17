# Fix Signatures

A Cities: Skylines II code mod that lets you choose the vehicle and storage limits for signature buildings.

## Usage

Open **Options > Fix Signatures** and configure:

- **Maximum vehicles**: 1–100, default 10.
- **Maximum storage (tonnes)**: 10–5,000 t, default 300 t.

Changes apply during gameplay to existing signature buildings and to buildings placed later.

Only signature buildings with a transport-company component are changed. Service vehicle capacities, ordinary zoned companies, and cargo stations are left untouched.

## Build

Install and initialize the Cities: Skylines II modding toolchain in-game, then build `Fix-Signatures.slnx` with Visual Studio or `dotnet build`.

The project also accepts the toolchain path as an MSBuild override:

```powershell
dotnet build Fix-Signatures.slnx --configfile NuGet.Config -p:CSIIToolPath="C:\path\to\.ModdingToolchain"
```
