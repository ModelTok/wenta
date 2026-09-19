# wenta

Ductwork design in C# — sizing, pressure-drop, fitting losses, network
solving, balancing, acoustics, insulation, fans, BOM/KNR — as a
dependency-free .NET library (**Wenta.Core**) and a **ZWCAD 2021 plugin**
(**WentaZwcad**) that compiles the library in. One DLL, no runtime
Python/Mojo/Rust/WASM.

```
csharp/Wenta.Core/         the library (35 modules, bare-csc, net48-compatible)
csharp/Wenta.Core.Tests/   console test runner + frozen CSV parity vectors
csharp/catalogs/           open ζ-catalog format + KNR mapping (FORMAT.md, examples)
docs/                      user guide, EN + PL
zwcad-plugin/              WentaZwcad — ZWCAD 2021 plugin, CUIX ribbon, ROADMAP.md
```

## Quick start

```cmd
just csharp-parity      # build Wenta.Core + run the whole test suite
just zwcad-build        # WentaZwcad.dll + Wenta.CUIX (needs the ZWCAD 2021 SDK)
```

Requires VS 2022 Build Tools (Roslyn `csc.exe`; any VS 2022 edition is
found via `vswhere`) and .NET Framework 4.x. No NuGet, no SDK-style
projects — `csharp/build.cmd` lists the files explicitly.

```csharp
using Wenta;

// Size a round duct for 0.1 m³/s at a target velocity of 4 m/s.
SizingResult r = Sizing.VelocityMethod(0.1, Sizing.ShapeRound, targetVelocity: 4.0);

// Solve a small network end to end.
var net = new Network { Name = "example" };
net.Add("ahu",  new Source("AHU"));
net.Add("duct", new RigidDuct("duct", new Round(0.2), length: 20));
net.Add("term", new Terminal("terminal", flowrate: 0.1));
net.Connect("ahu", "duct"); net.Connect("duct", "term");
double criticalPathPa = net.Solve();   // standard air by default
```

## Modules (`csharp/Wenta.Core`)

| Area | Files |
|---|---|
| Core physics | `Units` `Fluid` `Geometry` `Physics` (Swamee–Jain + Colebrook, losses, flex) |
| Size tables | `StandardSizes` (EN 1505/1506) · `Standards` (selectable EN / ASHRAE / DIN) |
| Sizing | `Sizing` — velocity / equal-friction / budget / noise / aspect-ratio, round + rect · `BatchSizing` (request lists, standard snapping) |
| Fittings | `FittingsLibrary` (23 correlations) · `Elbow` (round-elbow spline) · `ReCorrections` (Re/size ζ corrections) · `Catalog` (open ζ-catalog + vendor merge, spec in `csharp/catalogs/FORMAT.md`) |
| Network | `Components` `Network` `Solver` (flow propagation, ΔP, critical path, cycles) · `Topology` (polylines → network, flatten) · `NetworkJson` (versioned JSON round-trip) |
| Reporting | `Results` `Analysis` (per-branch report) `PressureReport` (critical-path ΔP) `Marking` (branch numbering) `Bom` + `KnrMap` (configurable KNR codes) + `BomExport` (JSON/XLSX) |
| Exchange | `IfcExport` (IFC4 duct segments / fittings / terminals from a traced system) |
| Engineering | `Balancing` `Room` (ACH) `Sound` (regenerated noise, NC) `Fan` (curves, duty point, power) `Insulation` (condensation, heat loss) `Electrical` |
| Fabrication | `Fabrication` (area, weight, cutting schedule) `Development` (flat patterns) `Clash` (segment clearance) |
| Project | `Settings` (`ProjectSettings`, `UnitSystem`) |

See `csharp/README.md` for the test methodology and `zwcad-plugin/README.md`
for the plugin commands (`WENTADUCT`, `WENTACATALOG`, `WENTABOM`,
`WENTAPANEL`, `WENTAHELLO`) and ZWCAD platform notes.

## Roadmap

`zwcad-plugin/ROADMAP.md`, mapped to GitHub issues in #62. Ground rules:
every engineering function lands in `Wenta.Core` first with tests; the
plugin ships as one DLL.

## History

This repository started as the Python `wenta` reference library, gained a
Mojo port (`wentamojo`) and a Rust port (`venti`), and was then consolidated
into C# for the CAD plugin stack. The Python, Mojo and Rust trees were
removed in issue #64 once every module had a C# counterpart with tests;
their final state is tagged `legacy-py-mojo-rust`.

## Bibliography

- ASHRAE Handbook — Fundamentals
- Hendiger, Ziętek, Chludzińska: *Wentylacja i Klimatyzacja — Materiały pomocniczne do projektowania*
- Swamee & Jain (1976): *Explicit equations for pipe-flow problems*
- Colebrook–White equation (friction factor correlation)
- Idelchik: *Handbook of Hydraulic Resistance*

## License

MIT
