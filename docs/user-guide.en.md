# Wenta — user guide (English)

This guide describes what exists in the repository today: the `Wenta.Core`
C# library and the `WentaZwcad` plugin for ZWCAD 2021. Everything below is
taken from the source code, the READMEs and the test suite; commands or
features that are only planned are listed under "Roadmap", not documented
as if they existed.

Polish version: `docs/user-guide.pl.md`.

---

## 1. What Wenta is

Wenta is a ductwork (HVAC) engineering toolkit in C#:

- **`Wenta.Core`** (`csharp/Wenta.Core`, namespace `Wenta`) — a
  dependency-free .NET library: duct sizing, friction and fitting losses,
  network solving with critical path, balancing, acoustics, fans,
  insulation, room air balance, bill of materials with KNR codes, an open
  ζ-catalog format, JSON round-trip of networks, topology tracing, clash
  detection and fabrication quantities. It targets .NET Framework 4.x and is
  built with the plain Roslyn `csc.exe` (no NuGet, no SDK-style projects).
- **`WentaZwcad`** (`zwcad-plugin/`) — a ZWCAD 2021 plugin. Every file of
  `Wenta.Core` is compiled *into* `WentaZwcad.dll`, so the deployment is a
  single DLL plus a ribbon file (`Wenta.CUIX`) and one example catalog.
  There is no Python, Mojo, Rust or WASM at run time.

All library quantities are SI: metres, m²/m³, m³/s, Pa, m/s, W, kg.
Catalog and size tables use millimetres where stated. `Units.cs` provides
converters for IP units (`CfmToM3s`, `InwcToPa`, `FtToM`, `InToM`,
`FpmToMs`, `FToC` and their inverses).

Licence: MIT. Repository: <https://github.com/ModelTok/wenta>.

---

## 2. Installing the plugin

**Requirement: ZWCAD 2021, 64-bit.** The plugin links against
`ZwManaged.dll` and `ZwDatabaseMgd.dll` from
`C:\Program Files\ZWSOFT\ZWCAD 2021` and is compiled with `/platform:x64`.
Other ZWCAD versions are not covered by the build script or the registry
installer.

### 2.1 Build prerequisites

- Visual Studio 2022 Build Tools (or any VS 2022 edition; `csc.exe` is found
  via `vswhere`).
- .NET Framework 4.x (the build references `System.dll`, `System.Core.dll`,
  `System.Windows.Forms.dll`, `System.Drawing.dll`,
  `System.Web.Extensions.dll`).
- ZWCAD 2021 installed in the default location (for the managed API DLLs).
- Optionally `just` for the recipes in the root `justfile`.

### 2.2 Build

```cmd
just csharp-parity      # csharp\build.cmd + run Wenta.Core.Tests.exe
just zwcad-build        # zwcad-plugin\build.cmd -> bin\WentaZwcad.dll + Wenta.CUIX
```

`zwcad-plugin\build.cmd` produces `WentaZwcad.dll`, copies
`csharp\catalogs\example-generic.json` next to it and builds `Wenta.CUIX`
with `tools\MakeCuix.cs`. Run the library tests first; the test runner ends
with `==== N passed, 0 failed ====`.

### 2.3 Install (`install.ps1`, must run elevated)

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

The script refuses to run without administrator rights (it writes to HKLM).
It then:

1. copies `WentaZwcad.dll`, `Wenta.CUIX` and `example-generic.json` from the
   build output folder to `C:\ProgramData\WentaZwcad\`;
2. creates `HKLM\SOFTWARE\ZWSOFT\ZWCAD\2021\en-US\Applications\WentaZwcad`
   with `LOADCTRLS = 14` (startup + on-command + manual), `MANAGED = 1`,
   `LOADER = C:\ProgramData\WentaZwcad\WentaZwcad.dll` and a `DESCRIPTION`.

Note: `install.ps1` takes the three files from the `bin` folder next to the
script (`zwcad-plugin\bin`, i.e. the output of `build.cmd`) and aborts if
any of them is missing there.

### 2.4 Load the ribbon once

Inside ZWCAD run:

```
_.MENULOAD C:\ProgramData\WentaZwcad\Wenta.CUIX
```

ZWCAD remembers the partial CUIX per profile; a tab named **Wenta** appears
on the ribbon. (In a script, `MENULOAD` needs `FILEDIA` set to `0` first —
see `full_test.scr`.)

### 2.5 Uninstall (`uninstall.ps1`, elevated)

Removes the registry key and `C:\ProgramData\WentaZwcad`. The ribbon tab may
remain in the ZWCAD profile until it is unloaded with `MENULOAD`/`CUI`.

---

## 3. Commands reference

Five commands exist. Every command appends one line to the evidence log
`%TEMP%\wenta_zwcad_test.txt` (e.g. `WENTADUCT ok  200×200 mm  0,150 m3/s
3,75 m/s  0,9530 Pa/m`). Sizing and pressure-drop maths is done by
`Wenta.Core`; no formula lives in the plugin.

### 3.1 `WENTADUCT` — size and draw a duct cross-section

Prompts (defaults in angle brackets):

1. `Duct shape [Round/Rectangular] <Rectangular>`
2. `Design flow [m³/s]` — default `0.1`, must be positive
3. `Target velocity [m/s]` — default `4.0`, must be positive

It calls `Sizing.VelocityMethod(flow, shape, targetVelocity)`, which returns
the smallest EN 1505 (rectangular) or EN 1506 (round) standard section whose
velocity is at or below the target. It then draws in model space at the
origin:

- rectangular: a closed polyline `width × height` in millimetres;
- round: a circle;
- a `DBText` label above the section, text height 40, reading
  `<size>  <flow> m³/s  <velocity> m/s  <Δp> Pa/m`. The Pa/m figure is the
  straight-duct friction drop for that section (Swamee–Jain friction factor,
  absolute roughness 0.1 mm, standard air).

The outline receives XData under application name `WENTA`: the string
`duct`, the flow, the velocity, a shape code (`1` round, `0` rectangular)
and the size label. The command line echoes
`Sized: 200×200 mm · 0.150 m³/s · v=3.75 m/s (target 4.0) · 0.95 Pa/m`
(values from the verified headless run with `Rectangular`, `0.15`, `4.0`).

### 3.2 `WENTACATALOG` — load the open ζ-catalog

Loads `example-generic.json` from the folder containing `WentaZwcad.dll`
(after installation, `C:\ProgramData\WentaZwcad`). It prints the catalog
name and version, the number of entries (5 in the shipped file) and a demo
lookup: `rect_elbow 400×200 → ζ=… (source: …)` using `ZetaCatalog.Match`
and `ZetaCatalog.ZetaFor`. If the file is missing it prints
`No example catalog beside the DLL: <path>`; a malformed file prints
`Catalog load failed: <message>`. The command does not read the drawing.

### 3.3 `WENTABOM` — solve the reference network and export a BOM

This command does **not** read the drawing. It builds the fixed reference
tee network used by the parity vectors:

```
AHU -> round D315 duct, 20 m -> tee (ζ straight 0.1, ζ branch 0.4)
      -> straight: round D200 duct, 5 m -> terminal 0.06 m³/s
      -> branch:   flex D125, 3 m, 2 Pa/m  -> terminal 0.04 m³/s
```

It solves it (`Network.Solve`, critical path **7.63 Pa**; the test vector
value is 7.629497821799035 Pa), builds `Bom.Build(net)` (7 rows, total duct
length 28.0 m) and writes `bom.ToCsv()` to **`%TEMP%\wenta_bom.csv`**. The
CSV is semicolon-separated with a decimal point:

```
item;kind;description;length_m;area_m2;flow_m3s;knr_code
```

The `knr_code` column holds the built-in placeholder codes
(`KNR 2-08 0101 (configure)` etc.). The `area_m2` column is, as
implemented in `Bom`, cross-section area × length; the sheet-metal surface
area is available separately from `Fabrication.DuctSurfaceAreaM2`.

### 3.4 `WENTAPANEL` — dockable sizing palette

Opens a snappable palette set named **Wenta** (minimum 240 × 260 px) with:

- `Flow [m³/s]` — numeric field, 0.001 to 20, default 0.1;
- `Target v [m/s]` — list `2.0 2.5 3.0 4.0 5.0 6.0 7.5`, default 4.0;
- `Shape` — `Rectangular` (default) or `Round`;
- a live preview of the section `VelocityMethod` would pick, e.g.
  `→ rect 200x200mm   v = 3.75 m/s`;
- **Draw sized duct section** — sends a fully answered `WENTADUCT` to the
  active drawing;
- **Plugin info** — runs `WENTAHELLO`.

The palette no longer opens automatically at start-up; use the command or
the ribbon button.

### 3.5 `WENTAHELLO` — plugin information

Prints `Wenta duct plugin (wenta C# core, MIT). ZWCAD version: <version>` and
logs the ZWCAD version (verified: `21.10.21.0`).

### 3.6 Ribbon tab "Wenta"

One panel with five large buttons, each running one command:

| Button | Command |
|---|---|
| Duct Section | `WENTADUCT` |
| Wenta Panel | `WENTAPANEL` |
| Plugin Info | `WENTAHELLO` |
| Fitting Catalog | `WENTACATALOG` |
| BOM + KNR | `WENTABOM` |

### 3.7 Roadmap

Commands such as `WENTATRACE`, `WENTASIZE`, `WENTAPRESSURE`, `WENTASOUND`,
`WENTABALANCE`, `WENTAFAN`, `WENTAINSULATE` and `WENTAHELP` are planned, not
implemented; the library half of several of them already exists (see §4).
The plan is in `zwcad-plugin/ROADMAP.md` and the GitHub issues of
`ModelTok/wenta`.

---

## 4. Engineering reference (library)

Class names are in namespace `Wenta`. Where a test in
`csharp/Wenta.Core.Tests/Program.cs` fixes a number, it is quoted.

### 4.1 Air (`Fluid`)

`Fluid.StandardAir()` — dry air at 20 °C, 101 325 Pa: ρ = 1.204 kg/m³,
μ = 1.825e-5 Pa·s. `Fluid.AirAtAltitude(altitudeM, temperatureC)` — ISA
pressure to the tropopause, ideal-gas density, Sutherland viscosity.

### 4.2 Geometry (`Round`, `Rectangular`, `Geometry`)

Cross-sections carry `Area`, `HydraulicDiameter` (rectangular:
2·W·H/(W+H)) and `Perimeter`. `Geometry.EquivalentRoundDiameter(w, h)` is
the ASHRAE equivalent diameter 1.30·(a·b)^0.625/(a+b)^0.25.

### 4.3 Friction and losses (`Friction`, `Losses`, `Flex`)

- `Friction.FrictionFactor(Re, ε/Dh)` — Swamee–Jain explicit Darcy factor;
  laminar 64/Re below Re = 2300.
- `Friction.FrictionFactorColebrook(Re, ε/Dh)` — implicit Colebrook–White by
  fixed-point iteration seeded from Swamee–Jain (tolerance 1e-12, max 100
  iterations).
- `Losses.StraightPressureDrop(f, L, Dh, v, ρ)` — Darcy–Weisbach
  f·(L/Dh)·ρ·v²/2. `Losses.LocalPressureDrop(ζ, v, ρ)` — ζ·ρ·v²/2.
- `Flex.StretchCorrectionFactor(D, stretch%)` — ASHRAE Fundamentals curve fit
  for under-stretched flexible duct.

### 4.4 Standard sizes (`StandardSizes`, `Standards`, `Settings`)

`StandardSizes` holds EN 1505:2001 rectangular and EN 1506:2007 round tables
(mm) and `NearestRoundSize`. `Standards` adds ASHRAE/SMACNA (inch series in
mm) and DIN 24155 (Renard R10/R20) tables behind the `Standard` enum
(`En1505_1506`, `AsHrae`, `Din`) with `NearestRoundSizeFor`.
`ProjectSettings` stores project defaults (EN standard, 200 mm default
diameter, roughness 0.0001 m, 4 m/s, 1 Pa/m, noise space `office`, `Si` or
`Ip` unit system) with `Validate()`.

### 4.5 Sizing (`Sizing`) — five methods

All return the smallest EN standard section meeting the target (falling back
to the largest section) as `SizingResult { Section, Velocity,
PressureDropPerMeter }`:

| Method | Criterion |
|---|---|
| `VelocityMethod(Q, shape, vTarget = 4)` | velocity ≤ target |
| `EqualFrictionMethod(Q, ΔpPerM = 1, shape, ε, fluid)` | friction drop per metre ≤ target |
| `PressureDropBudget(Q, L, budgetPa, …)` | total drop over L ≤ budget (= equal friction at budget/L) |
| `NoiseLimitMethod(Q, spaceType, …)` | velocity ≤ `NoiseLimitsMs[spaceType]` (studio 2.5, bedroom 3.0, office 4.0, classroom 4.5, retail 5.0, industrial 7.5 m/s) |
| `AspectRatioMethod(Q, vTarget, aspectRatio = 2)` | rectangular sections with side ratio ≥ aspect ratio, smallest area meeting velocity |

### 4.6 Fitting loss coefficients ζ

- `FittingsLibrary` — correlations from ASHRAE Fundamentals, Hendiger and
  Idelchik: `ReducerRound`, `ExpanderRound`, `JunctionTeeBranch`,
  `JunctionTeeCombine`, `DamperButterfly` (0.1 fully open), `DiffuserCeiling`
  (0.4/areaThrow), `GrilleReturn` (0.25·(1+blockage)), `RectangularElbow`
  (Idelchik §6 with aspect correction), `MiteredElbow` (vaned cuts to 40 %).
- `ElbowRound(R, D, angle)` — Hendiger/Ziętek/Chludzińska round-elbow table
  (R/D 0.5–2.5, 20°–180°) interpolated with a not-a-knot bicubic spline.
- `ReCorrections` — mild multiplicative Reynolds and size corrections to a
  catalogue ζ, clamped to [0.75, 1.5] and [0.9, 1.3]; reference point
  Re = 50 000, D = 200 mm.
- `ZetaCatalog` — the open JSON catalog (spec `csharp/catalogs/FORMAT.md`):
  `Load`/`Parse`, `ById`, `Match(type, sizeMm)` (first match in file order,
  inclusive size window in mm), `ZetaFor` (catalog first, then the built-in
  correlation for `rect_elbow`, `mitered_elbow`, `damper`, `diffuser`,
  `grille`, `tee`; other types have no fallback and throw).
  `Merge(base, overrides)` layers catalogs by entry id, replacing in place
  and appending one line per override to `Warnings`:
  `id X: zeta A (source S1) overridden by zeta B (source S2)`. Every entry
  carries `source` provenance and an optional `knr` hint.

### 4.7 Network and solver (`Components`, `Network`, `Solver`)

Components: `Source` (AHU/fan, no drop), `RigidDuct` (Darcy–Weisbach,
default roughness 0.1 mm), `FlexDuct` (vendor Pa/m × length × stretch
factor), `TwoPortFitting` (ζ on the outlet), `Tee` (ports `combined`,
`straight`, `branch`, separate ζ per leg), `Terminal` (demanded flow,
optional section and ζ). `Network.Add(id, component)`,
`Network.Connect("a", "b.port")`, `Validate()`, `Solve(fluid)`:

1. terminal demands are propagated upstream in reverse topological order;
2. each component computes per-port velocity and pressure drop;
3. the critical path is the longest source-to-terminal path by node drop
   (`Solver.CriticalPath`, `CriticalPathPressureDrop`).

Cycles throw `network graph contains a cycle`. Solving is idempotent.
Worked values: the README chain (AHU → round D200, 20 m → terminal
0.1 m³/s) gives **14.13 Pa** (14.13473757973617); the reference tee network
of §3.3 gives **7.63 Pa**.

### 4.8 Results, analysis, marking

- `Results.ExtractResults(net)` — one `ComponentResult` per component
  (flow/velocity in and out, total drop); `ResultsAsCsv`, `ResultsSummary`.
- `Analysis.Analyze(net, fluid)` — solves the network and returns
  `CriticalDpPa` plus one `BranchInfo` per duct (flow, velocity, drop,
  regenerated noise, balancing ζ). The balancing ζ uses the branch's own
  drop as a proxy for available pressure — a documented approximation.
- `Marking.AssignBranchMarks(net)` — deterministic BFS branch numbers for
  every `RigidDuct` with size (mm) and flow; `MarksAsCsv`.

### 4.9 Balancing (`Balancing`)

`RequiredZeta(Δp, v, ρ)` = 2·Δp/(ρ·v²); `BalancingZeta(totalReq,
branchAvail, v, ρ)` returns the damper ζ that absorbs the surplus (0 when the
branch already meets its requirement; test: 30 Pa vs 10.736 Pa at 4 m/s →
ζ = 2.0); `DamperOpenPercentage(ζ)` inverts the butterfly-damper correlation.

### 4.10 Acoustics (`Sound`)

`RegeneratedNoiseRound(v, D, ρ)` — regenerated sound power level
Lw = 10 + 10·log10(ρ/ρ0) + 60·log10(v) − 20·log10(D) dB re 1e-12 W (a v⁶
Lighthill-type scaling; the constant is a calibration offset, not a
standard). `DuctPressureLevel(Lw, S, α)` — diffuse-field room equation
Lp = Lw + 10·log10(4(1−α)/(α·S)). `NcOk(spaceType, level)` /
`NcOkTarget(nc, level)` compare against `NoiseLimitsNc` (studio 25,
bedroom 25, office 35, classroom 35, retail 40, industrial 60).

### 4.11 Fans (`FanCurve`, `Fan`)

`FanCurve(name, FanPoint[])` — vendor static-pressure curve, strictly
increasing flows, piecewise-linear `StaticPressureAt(Q)` (throws outside the
tabulated range). `Fan.Power(Q, p, η)` = Q·p/η W (test: 1.5 m³/s, 800 Pa,
η 0.4 → 3000 W). `Fan.Margin(curve, Q, pReq)` (null outside range) and
`Fan.PickFan(curves, Q, pReq)` — first fan with non-negative margin.

### 4.12 Insulation (`Insulation`)

Steady-state cylindrical resistance network per metre (inner film, insulation
ln(Do/D)/(2πλ), outer film), following EN ISO 12241 / ASHRAE practice.
`RequiredThicknessCondensation(Tair, Tdew, Tamb, λ, D, hi, he)` grows the
thickness in 1 mm steps (max 250 mm) until the surface stays above the dew
point; `RequiredThicknessHeatLoss(…, targetWPerM, …)`; `HeatLossWithInsulation`;
`SelectThickness(t)` rounds up to 20/30/40/50/60/80/100/120 mm (0.045 m →
0.05 m). Materials table: mineral wool 0.035, PE foam 0.040, EPDM/NBR 0.038,
PIR 0.024, PU foam 0.028 W/(m·K). Defaults hi = 10, he = 8 W/(m²·K).

### 4.13 Rooms and air-change rate (`Room`, `Units`)

`RoomBalance(supply, exhaust)` — net flow, `IsBalanced(tolerance)`,
`ImbalanceFraction`. `RoomBalanceSet` totals many rooms and renders CSV
`name,supply_m3s,exhaust_m3s,net_m3s,ach`. `Units.AirChangesPerHour(Q, V)` =
Q·3600/V (0.1 m³/s in 100 m³ → 3.6 h⁻¹).

### 4.14 BOM, KNR mapping and export (`Bom`, `KnrMap`, `BomExport`)

`Bom.Build(net)` or `Bom.Build(net, knrMap)` — one row per component with
kind (`duct`, `flex`, `fitting`, `terminal`, `source`), description, length,
area, flow and KNR code. Without a map the placeholder codes in `Bom.KnrMap`
are used. With a `KnrMap` loaded from JSON (§5.2), overrides are tried first
(exact `kind` + optional `shape` `round|rectangular` + optional `type`
`tee|inline|<class>`), then `codes[kind]`; anything unmapped gets an empty
code and is listed once in `Bom.Unmapped` — a code is never invented.
Exports: `Bom.ToCsv()` (semicolon CSV), `BomExport.ToJson`/`SaveJson`
(schema_version 1, rows + totals) and `BomExport.ToXlsx`/`SaveXlsx` (single
sheet, header row, rows, `TOTAL` row; a hand-rolled OOXML package with no
external dependency).

### 4.15 Network JSON (`NetworkJson`)

`Serialize(net, meta)` / `Save` and `Parse(json, out meta)` / `Load` —
versioned JSON of a network (`schema_version` 1). Each component carries
`id`, `wenta_class`, a stable `guid` (generated when missing), reserved
`drawing_scope`, `name` and its constructor arguments in snake_case.
Connections are `"from": "id.port"`, `"to": "id.port"`. Newer schema
versions, unknown classes, duplicate ids and missing required fields are
rejected with `WentaException`. Round-trip preserves the critical path
bit-for-bit (tests: 7.63 Pa and 14.13 Pa).

### 4.16 Topology (`Topology`)

`Topology.Trace(polylines, TraceOptions)` coalesces 2D centreline polylines
(snap tolerance default 1e-4 m) into a tree network of `Source` (first
degree-1 end), `RigidDuct` chains (`duct0`, `duct1`, …), `Tee` at degree-3
vertices and `Terminal` at the other ends (`term0`, …). Diameters and terminal
flows come from `TraceOptions.Diameters`/`Flows` (default diameter 0.2 m).
Degree ≥ 4 vertices and loops are rejected. `TracedSystem.Flatten()` returns
`DuctSegment`s for drawing; `TotalLengthM()` sums chain lengths. Tee legs
are assigned by traversal order, not geometry.

### 4.17 Clash detection (`ClashDetection`)

`FindClashes(segments, clearanceM)` — exact minimum distance between 2D
centreline segments; a pair clashes when distance < (Da + Db)/2 + clearance.
Each pair is reported once, ordered by id; `ClashesAsCsv` writes
`a,b,distance_m`.

### 4.18 Fabrication and developments (`Fabrication`, `Development`)

`Fabrication.DuctSurfaceAreaM2(section, L)` = perimeter × L;
`DuctWeightKg(area, gaugeMm, density)` with steel 7850 kg/m³ by default;
`FabricationBreakout` (straight vs fittings length); `CuttingSchedule`
(per-component totals, sorted). `Development.RoundDuctDevelopment`,
`RoundElbowDevelopment`, `ReducerConeDevelopment` return `FlatPiece`
estimates (width, length, area) — explicitly approximations, without seam
allowances.

### 4.19 Electrical schedule (`Electrical`)

`ElectricalData(componentId, deviceType, powerW)` with optional voltage,
power factor, frequency; `Current()` = P/(U·cos φ). `ElectricalSchedule`
totals; `Electrical.AsCsv` renders
`component_id,device_type,power_w,power_kw,voltage_v,current_a,power_factor,frequency_hz`.

---

## 5. Data files you can edit

All three formats are UTF-8 JSON with an integer schema version; a reader
rejects a file declaring a newer version than it understands, and unknown
keys are ignored. Numbers use a decimal point regardless of Windows locale.

### 5.1 ζ-catalog (`csharp/catalogs/FORMAT.md`, version 1)

Minimal valid file (from the spec):

```json
{
  "name": "my-office-defaults",
  "version": 1,
  "fittings": [
    { "id": "round-elbow-rd1.0", "type": "round_elbow", "zeta": 0.24,
      "source": "Hendiger tab. 4.3, R/D = 1.0" }
  ]
}
```

Fields per entry: `id` (required, unique), `type` (well-known values
`rect_elbow`, `round_elbow`, `mitered_elbow`, `tee`, `tee_straight`,
`reducer`, `damper`, `diffuser`, `grille`), `size_min_mm`/`size_max_mm`
(paired, `[w, h]` or `[d]`), `zeta` (required, numeric), `source`, `knr`,
`notes`. First match wins; a windowless entry matches every size of its
type. Shipped examples: `example-generic.json` (installed with the plugin),
`example-generic-round.json`, `example-vendor-style.json` (fictional
vendor). None of the example values is measured vendor data.

### 5.2 KNR mapping (`csharp/catalogs/knr-example.json`, schema_version 1)

```json
{
  "schema_version": 1,
  "edition": "KNR 2-08 example (configure per your edition)",
  "codes": {
    "duct": "2-08 01xx-A (placeholder: round sheet-metal duct)",
    "flex": "2-08 02xx-A (placeholder: flexible duct)",
    "fitting": "2-08 03xx-A (placeholder: generic fitting)",
    "terminal": "2-08 04xx-A (placeholder: air terminal)",
    "source": ""
  },
  "overrides": [
    { "match": { "kind": "duct", "shape": "rectangular" },
      "code": "2-08 01xx-B (placeholder: rectangular sheet-metal duct)" },
    { "match": { "kind": "fitting", "type": "tee" },
      "code": "2-08 03xx-T (placeholder: tee / branch piece)" }
  ]
}
```

Every code in the shipped file is a placeholder — configure it for your KNR
edition before using it for estimates. Keys starting with `_` are ignored,
so the file can document itself. An empty string means "deliberately no
position"; an absent kind leaves the code empty and is reported in
`Bom.Unmapped`. Load with `KnrMap.Load(path)` and pass to `Bom.Build`.

### 5.3 Network JSON (`NetworkJson`, schema_version 1)

The README chain network, as written by `NetworkJson.Serialize` (GUIDs
abbreviated):

```json
{
  "schema_version": 1,
  "name": "readme",
  "components": [
    { "id": "ahu",  "wenta_class": "Source",    "guid": "…", "drawing_scope": null, "name": "AHU" },
    { "id": "duct", "wenta_class": "RigidDuct", "guid": "…", "drawing_scope": null, "name": "duct",
      "cross_section": { "shape": "round", "diameter": 0.2 },
      "length": 20, "absolute_roughness": 0.0001 },
    { "id": "term", "wenta_class": "Terminal",  "guid": "…", "drawing_scope": null, "name": "terminal",
      "flowrate": 0.1, "zeta": 0 }
  ],
  "connections": [
    { "from": "ahu.outlet",  "to": "duct.inlet" },
    { "from": "duct.outlet", "to": "term.inlet" }
  ]
}
```

Classes: `Source`, `RigidDuct` (`cross_section`, `length`,
`absolute_roughness`), `FlexDuct` (`diameter`, `length`,
`pressure_drop_per_meter`, `stretch_percentage`), `TwoPortFitting`
(`cross_section`, `zeta`), `Tee` (`cross_section`, `zeta_straight`,
`zeta_branch`), `Terminal` (`flowrate`, optional `cross_section`, `zeta`).
Rectangular sections are `{ "shape": "rectangular", "width": w, "height": h }`.
Geometry in metres, flows in m³/s. Optional constructor arguments may be
omitted. On input, `type`/`source`/`target` are accepted as legacy aliases
of `wenta_class`/`from`/`to`.

---

## 6. Troubleshooting

Facts about the ZWCAD 2021 platform recorded in `zwcad-plugin/README.md`:

- **Plugin does not load.** Registration must be under **HKLM**
  (`LOADCTRLS = 14`); HKCU entries are ignored. `install.ps1` therefore
  needs UAC elevation. Check that `LOADER` points at an existing
  `WentaZwcad.dll`.
- **Wrong platform.** The managed API (`ZwManaged.dll`, `ZwDatabaseMgd.dll`)
  is mixed-mode x64; the DLL must be built with `/platform:x64`, which
  `build.cmd` does.
- **`NETLOAD` or `MENULOAD` fails inside a script.** Set `FILEDIA` to `0`
  before them (see `full_test.scr`) and back to `1` afterwards.
- **Ribbon tab missing.** Run `MENULOAD` on `Wenta.CUIX` once. The tab
  relies on `DefaultDisplay="AddToWorkSpace"` and
  `WorkspaceBehavior="MergeOrAddTab"` in the CUIX; `uia-check.ps1` verifies
  its presence in the UI tree.
- **`WENTACATALOG` says no example catalog.** The command looks for
  `example-generic.json` next to the DLL; `install.ps1` copies it to
  `C:\ProgramData\WentaZwcad`.
- **What did the plugin actually do?** Every command logs to
  `%TEMP%\wenta_zwcad_test.txt`; `WENTABOM` writes `%TEMP%\wenta_bom.csv`.
- **Exceptions.** `ZwSoft.ZwCAD.Runtime.Exception` shadows `System.Exception`
  in plugin code; library errors are `Wenta.WentaException` with a message
  naming the offending field, file or component.
- **Known limitation.** `WENTABOM` solves a built-in reference network
  rather than the drawing; reading the network from the drawing is roadmap
  work (`WENTATRACE` / `WENTAREAD`).

Report issues at <https://github.com/ModelTok/wenta/issues>. Include the
ZWCAD version printed by `WENTAHELLO`, the relevant lines of
`%TEMP%\wenta_zwcad_test.txt` and, for library problems, the
`WentaException` message.
