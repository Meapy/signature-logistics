# Fix Signatures

A Cities: Skylines II code mod that lets you choose how many delivery vehicles each signature building may own.

## Usage

Open **Options > Fix Signatures**, then set **Maximum vehicles** from 1 to 100. The vanilla default is 5. Changes apply to existing signature buildings and to buildings placed later.

Only signature buildings with a transport-company component are changed. Service vehicle capacities, ordinary zoned companies, and cargo stations are left untouched.

## Build

Install and initialize the Cities: Skylines II modding toolchain in-game, then build `Fix-Signatures.slnx` with Visual Studio or `dotnet build`.

The project also accepts the toolchain path as an MSBuild override:

```powershell
dotnet build Fix-Signatures.slnx --configfile NuGet.Config -p:CSIIToolPath="C:\path\to\.ModdingToolchain"
```
