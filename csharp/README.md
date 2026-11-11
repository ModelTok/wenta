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
  Solver.cs Sizing.cs BatchSizing.cs Catalog.cs KnrMap.cs Bom.cs BomExport.cs
  Results.cs Analysis.cs PressureReport.cs Marking.cs Balancing.cs Room.cs
  Sound.cs Fan.cs FanCatalog.cs Insulation.cs Electrical.cs Fabrication.cs Development.cs
  Clash.cs Topology.cs IfcExport.cs NetworkJson.cs NetworkDrawing.cs Settings.cs
  Attenuators.cs Markdown.cs NetworkEdit.cs Schedule.cs Annotation.cs
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
- **`DrawingModel.cs`** — host-agnostic adapter for the ZWCAD network
  commands: the `WENTA` XData tag (`WentaTag`, v2 with class + GUID, legacy
  records still read), drawn centrelines and markers in millimetres →
  `DrawingTrace` (network plus entity ↔ component maps), per-run sizing
  (`DrawingSizing`), damper settings (`DrawingBalancing`), room sound
  (`DrawingSound`) and insulation (`DrawingInsulation`). No new math; it
  maps data onto the modules above.
- **`FanCatalog.cs`** — the open fan-curve catalog JSON (`FanCatalog.Load`,
  example `catalogs/example-fans.json`) and `FanSelection` (duty point of a
  drawn network, every fan's margin and power, `Fan.PickFan`) for `WENTAFAN`.
- **`NetworkDrawing.cs`** — network JSON ⇄ drawing for `WENTAWRITE` /
  `WENTAREAD`: lays a network out to scale (or reuses its stored layout),
  builds each entity's tag record, and reads tagged entities back into a
  network, refusing orphaned tags, copied GUIDs and inconsistent records.
- **`Annotation.cs`** — the `WENTALABEL` smart labels: `Annotation.Build`
  gives each duct of a solved network its id, `Marking` branch number,
  size, flow, velocity and Pa/m; `LabelStyle` is the style JSON (visible
  fields, text height, offset, decimals) with validation.
- **`Schedule.cs`** — the `WENTASCHEDULE` table (`Schedule.Build`: Bom
  rows with `Marking` branch numbers, sorted by kind and id, invariant
  culture, totals, CSV) and the `WENTAFLAT` layout
  (`Schedule.FlatPatterns`: round rigid ducts developed with
  `Development.RoundDuctDevelopment`, side by side, others skipped).
- **`NetworkEdit.cs`** — editing a drawn network for `WENTAPROPS`,
  `WENTAMOVE`, `WENTASTRETCH` and `WENTAREFIT`: one property (diameter
  snapped to EN 1506, width, height, length, flow, ζ), stretch a duct and
  move what is downstream, move a point component with its ducts' ends,
  and `ReFlow` (new terminal flow → `ReFit.Apply` on the legs feeding it → damper ζ per terminal
  via `Balancing.BalancingZeta`), each returning the new network and layout.
- **`Attenuators.cs`** — the attenuator catalog JSON (`AttenuatorCatalog`,
  example `catalogs/example-attenuators.json`, fictional) and
  `AttenuatorSelection` for `WENTAATTEN`: room sound per terminal net of
  existing attenuators, lowest-ζ pick that covers the excess, splice in
  front of the terminal, re-solve.
- **`Markdown.cs`** — a small Markdown → HTML converter (headings with
  ids and a table of contents, lists, fences, pipe tables; raw HTML is
  escaped) used at build time to turn the user guides into the HTML help
  `WENTAHELP` opens.

The ZWCAD plugin (`../zwcad-plugin`) compiles every file in `Wenta.Core/`
into its own DLL — keep `zwcad-plugin/build.cmd`'s file list in sync with
`build.cmd` when adding a module.
