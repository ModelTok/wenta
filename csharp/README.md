# Wenta.Core — the C# ductwork engineering library

C# is the language of the CAD/BIM plugin stack (ZWCAD, AutoCAD, Revit are all
.NET hosts). `Wenta.Core` is the complete, dependency-free ductwork design
library — sizing, friction, fittings, network solver, catalogs, BOM, and the
Phase-4 engineering modules (sound, balancing, fans, insulation, rooms,
fabrication, clash, topology) — compiled with the bare-csc toolchain and
loadable in ZWCAD as a single DLL.

It began as a port of the Python `wenta` reference, its Mojo port and the
Rust `venti` library. Those three implementations were removed from the
repository in the C#-only migration (GitHub issue #64); their last state is
tagged `legacy-py-mojo-rust` in git history. **Wenta.Core is now the source
of truth.**

## Layout

```
Wenta.Core/           pure C# library (compiles standalone, net48-compatible)
  Units.cs Fluid.cs Geometry.cs Physics.cs StandardSizes.cs Standards.cs
  FittingsLibrary.cs Elbow.cs ReCorrections.cs Components.cs Network.cs
  Solver.cs Sizing.cs Catalog.cs Bom.cs Results.cs Analysis.cs Marking.cs
  Balancing.cs Room.cs Sound.cs Fan.cs Insulation.cs Electrical.cs
  Fabrication.cs Development.cs Clash.cs Topology.cs Settings.cs
Wenta.Core.Tests/     console test runner (Program.cs, no xUnit needed)
  vectors/            CSV parity vectors (frozen golden fixtures, see below)
catalogs/             open ζ-catalog format: FORMAT.md (spec) + example-generic,
                      example-generic-round, example-vendor-style (fictional vendor)
build.cmd             builds core + tests, copies vectors
```

## Build & test

```cmd
build.cmd
bin\Wenta.Core.Tests.exe        # => "==== N passed, 0 failed ===="
```

or `just csharp-parity` from the repo root. `build.cmd` uses the VS 2022
Build Tools `csc.exe` and falls back to `vswhere` for any other VS 2022
edition (that is how `.github/workflows/csharp.yml` runs it on
`windows-latest`).

## What the tests are

Two kinds of assertions, both in `Wenta.Core.Tests/Program.cs`:

- **Parity vectors** (`vectors/*.csv`) — units, fluid, geometry, friction,
  losses, flex, EN sizes, fittings, elbow spline, sizing and solver. They
  were generated from the Python/Mojo oracle while it still lived in this
  repository (see the `legacy-py-mojo-rust` tag; the generator was
  `csharp/tools/gen_vectors.py`). They are now frozen golden fixtures: a
  change that alters them is a deliberate behaviour change, not a
  regeneration. Tolerances: 1e-12 relative everywhere; elbow grid points
  1e-9, elbow intermediate points 2e-4 (separable not-a-knot bicubic vs
  FITPACK).
- **Closed-form regression tests** — catalog, BOM, balancing, room, and
  every module ported from `venti` (standards, Re/size corrections,
  settings, results, analysis, marking, fabrication, development, clash,
  topology, fan, insulation, sound, electrical). These were transcribed
  from the Rust modules' inline unit tests before the Rust tree was
  removed, with the same tolerances.

## Beyond the reference surface

- **`Catalog.cs`** — the open ζ-catalog format (JSON, spec in
  `catalogs/FORMAT.md`): pluggable manufacturer loss tables with provenance
  and KNR codes; `ZetaCatalog.Merge` layers vendor catalogs over a base by
  entry id and records every override in `Warnings`.
- **`Bom.cs`** — bill of materials with KNR-ready rows from a solved network.
- **`Standards.cs`** — selectable EN 1505/1506, ASHRAE/SMACNA and DIN 24155
  size tables (`Standard` enum) on top of the canonical EN tables in
  `StandardSizes.cs`.
- **`Topology.cs`** — trace 2D polylines into a `Network` (tee detection at
  shared vertices) and flatten a network back to draw segments; the
  library half of the WENTATRACE command.

The ZWCAD plugin (`../zwcad-plugin`) compiles every file in `Wenta.Core/`
into its own DLL — keep `zwcad-plugin/build.cmd`'s file list in sync with
`build.cmd` when adding a module.
