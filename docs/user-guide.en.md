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

Fourteen commands exist. Every command appends one line to the evidence log
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

The outline receives a `WENTA` tag (XData, application name `WENTA`): the
class `duct`, the schema marker `v2`, a GUID, then key/value string pairs
(`geometry` = `section`, `shape`, `d_mm` or `w_mm`/`h_mm`, `flow_m3s`,
`velocity_ms`), all written with the invariant culture. The network commands
(§3.6–3.11) use the same layout. The record of earlier versions (`duct`,
flow, velocity, shape code, size label) is still read. The command line
echoes
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

### 3.6 Drawing a network for the network commands

`WENTATRACE`, `WENTAPRESSURE`, `WENTASIZE` and `WENTABALANCE` read the
network from the drawing. Draw it as follows (units: millimetres):

- **Centrelines** — one `LINE`, open `LWPOLYLINE` or `ARC` per duct run.
  Runs meet at shared end points (snap tolerance 1 mm). A point where three
  runs meet becomes a tee; four or more runs at one point, and closed loops,
  are rejected. Arcs and polyline bulges are followed as chords of at most
  5°. Closed polylines (such as the `WENTADUCT` outline) are ignored.
- **Markers** — place them with `WENTATAG` (§3.7): one *source* at the
  air-handling-unit end, one *terminal* with its design flow at every other
  open end, and optionally *dampers* on branch runs. A terminal marker must
  lie within 150 mm of an open end; an open end without a terminal flow stops
  the command and lists the positions to tag.
- **Sizes** — a run takes its section from the `WENTA` tag written by
  `WENTASIZE` (or an older `WENTADUCT` record); untagged runs are Ø200.

At every tee the leg that continues the incoming run's direction is the
straight leg (ζ = 0) and the other is the branch (ζ = 0.5), so the order and
direction in which the lines were drawn does not matter. Select the whole
network, markers included, when a command asks for objects (`ALL` works:
labels and overlays are skipped).

### 3.7 `WENTATAG` — place a terminal, source or damper marker

Prompts: `Marker [Terminal/Source/Damper] <Terminal>`, the position, and for
a terminal `Terminal design flow [m³/s] <0.1>`. It draws a Ø200 circle on
layer `WENTA-TAG` with a text label (`T 0.060 m³/s`, `SRC` or `DMP`) and
stores a `WENTA` tag on it. Tagging again within 150 mm of an existing
marker edits that marker (class, flow, position) instead of adding a second
one, and clears its old balancing result.

### 3.8 `WENTATRACE` — build the network from the drawing

Builds the `Wenta.Network` from the selection (`DrawingTrace.Build` over
`Topology.Trace`), solves it with standard air and prints the number of
runs, tees and terminals, the total duct length, the flow at the source, the
critical-path Δp and one line per run (`duct0  5.00 m  Ø200  0.100 m³/s`).
The network is saved to **`%TEMP%\wenta_trace.json`** (Network JSON, §5.3);
components that came from tagged entities carry the entity's GUID.

### 3.9 `WENTAPRESSURE` — critical-path pressure drop

Prompts for the site altitude (`<0>` m; above 0 the air density comes from
`Fluid.AirAtAltitude`). It prints the `PressureReport` table of the
critical path and a per-component table of flow, velocity and Δp
(`Analysis.Analyze`), writes the report CSV to **`%TEMP%\wenta_pressure.csv`**
and redraws the critical runs as a 40 mm wide polyline overlay on layer
`WENTA-CRIT` (cleared and redrawn on every run). The critical Δp is stored in
the drawing, so the next run also reports the change since the previous one,
for example `previous run 4.43 Pa, change +3.57 Pa`.

### 3.10 `WENTASIZE` — size every run of the network

Prompts for the method and its target:

| Method | Prompt (default) | Library call |
|---|---|---|
| `Velocity` | target velocity (4.0 m/s) | `Sizing.VelocityMethod` |
| `EqualFriction` | target friction (1.0 Pa/m) | `Sizing.EqualFrictionMethod` |
| `Budget` | pressure budget per run (10 Pa) | `Sizing.PressureDropBudget` |
| `Noise` | space type (`office`) | `Sizing.NoiseLimitMethod` |
| `Aspect` | target velocity (4.0 m/s), aspect ratio (2.0) | `Sizing.AspectRatioMethod` (rectangular) |

and the shape (`Round` default, or `Rectangular`). The solver propagates the
terminal flows to every run, `BatchSizing.Size` sizes each run and snaps it
to the EN 1506 / EN 1505 series. Each centreline gets the size, flow and
velocity in its `WENTA` tag and a label on layer `WENTA-LABEL`, for example
`Ø200  0.100 m³/s  3.18 m/s`. Running it again updates the same labels in
place (the label's handle is kept in the tag) instead of adding new ones.
It ends with the critical-path Δp of the resized network.

### 3.11 `WENTABALANCE` — damper settings per terminal

Prompts for the site altitude, solves the network and calls `ReFit.Hints`:
for every terminal, the pressure surplus over the critical path, the damper
ζ that absorbs it and the butterfly-damper opening (`Balancing`, §4.9). The
values are written into the terminal markers and into every damper marker on
a run that feeds a terminal directly, and shown in their labels
(`T 0.060 m³/s  ζ=0.15 93% open`). A branch whose damper would have to close
completely is flagged `CHANGE HARDWARE` (smaller duct, orifice or another
damper type). The schedule is written to **`%TEMP%\wenta_balance.csv`**:

```
marker,class,terminal_id,run_id,available_dp_pa,required_dp_pa,surplus_dp_pa,damper_zeta,damper_open_percent,needs_hardware
```

### 3.12 Checking the network commands headless

`zwcad-plugin/network_test.scr` draws the reference layout (5 m main from
(0,0) to a tee at (5000,0); 3 m straight run to a 0.06 m³/s terminal; 3 m
branch to a 0.04 m³/s terminal; a damper on the branch) and runs every
network command on it. Load the plugin first (installed, or `NETLOAD` in a
wrapper script with `FILEDIA 0`). Verified on ZWCAD 2021: `WENTATRACE`
4.43 Pa with the default Ø200; after `WENTASIZE` velocity 4 m/s (Ø200 main,
Ø150 straight, Ø125 branch) 8.00 Pa, and a second `WENTASIZE` updated the
three labels without adding any; `WENTABALANCE` ζ 0.15 / 93 % open for the
straight-run terminal, while the branch is the critical path and its damper
stays fully open. It then runs `WENTAFAN` (extra 150 Pa: `ExampleFan EF-160`
at 0.12 m³/s and 160.7 Pa), `WENTAINSULATE` twice (20 mm mineral wool on
every run, the second run updating the three labels), `WENTASOUND` and
`WENTAHELP`. These are the values `Wenta.Core` computes for the layout.
Finally it checks the round trip: `WENTAWRITE` draws the traced network at
(0,−20000) (7 components), `WENTAREAD` reads it back (4.43 Pa, the
`WENTATRACE` value), a second `WENTAWRITE` updates the 7 entities in place
and the second `WENTAREAD` JSON is byte-identical to the first; with the
point components erased `WENTAREAD` refuses with orphaned tags, and after
lengthening one drawn duct by 1 m it reports one edit and 5.14 Pa.
Last, `WENTAATTEN` (studio, 50 m², α 0.2, the example catalog) puts
`ExampleAtten EA-200-1200` and `EA-200-600` in front of the two
terminals, leaves no room over the limit and raises ΔP from 5.14 to
6.84 Pa; `WENTAREAD` then reads 9 components and 6.84 Pa.
The editing commands follow on the same drawing: `WENTAPROPS` `duct1`
diameter 270 is snapped up to Ø300 (6.84 → 6.12 Pa), `WENTASTRETCH`
`duct1` to 4 m moves the attenuator and terminal at its end (6.16 Pa),
`WENTAMOVE` `tee0` by (0, −500) lengthens the three joined ducts
(6.17 Pa) and `WENTAPROPS` `term0` flow 0.05 m³/s gives 7.22 Pa;
`WENTAREAD` then reads the 9 components with no length edits, 7.22 Pa,
and every GUID unchanged. Last, the drawing is erased,
`example-dampers-network.json` is drawn at (0,−40000) and `WENTAREFIT`
`t1` 0.2 m³/s (velocity 4 m/s) re-sizes the main and the straight run
feeding it from Ø250 / Ø200 to Ø300 (the tee and damper `dS` follow, the
branch to `t2` keeps Ø160), sets `dS` to ζ 0.05 with the branch damper
`dB` open, and takes 2.59 → 3.70 Pa with no residual imbalance in about
6 ms; `WENTAREAD` reads 3.70 Pa back with no edits.
`WENTASCHEDULE` then draws the 12-row schedule table at (20000,−40000)
(9 items, 11.000 m, totals as `Bom`), `WENTAPROPS` `t2` flow 0.05 m³/s
(4.54 Pa) and a second `WENTASCHEDULE` update the same table in place
(t2 0.0500 m³/s), `WENTAFLAT` at (0,−60000) draws the three round ducts
flat (9.048 m² of sheet, π·D·L) and `WENTAREAD` reads 4.54 Pa with no
edits.
Last, `WENTALABEL` labels the three ducts, `WENTAPROPS` `branch` Ø200
refreshes one label by itself and a following `WENTALABEL` finds 0
changed; `WENTALABEL` `example-label-style.json` restyles all three in
one run, `WENTASTRETCH` `branch` to 4 m moves its label, `WENTALABEL`
again finds 0 changed, and `WENTAREAD` reads 3.89 Pa with no edits.

### 3.13 `WENTAFAN` — fan at the duty point

Select the network (as for §3.8). Prompts: site altitude, `Extra pressure
outside the ductwork (AHU, filters, coils) [Pa] <0>` and the fan catalog
file (Enter takes `example-fans.json` beside the DLL). The duty point is the
total terminal flow and the critical-path Δp plus the extra pressure
(`FanSelection.DutyPoint`). Every fan in the catalog is listed with the
pressure its curve gives at the duty flow, the margin, and the power
Q·p/η at the duty point when the fan meets it and has an efficiency
(`Fan.Margin`, `Fan.Power`, §4.11). The selected fan, marked `*`, is the
first one in catalog order that meets the duty point (`Fan.PickFan`); it is
written into the source marker's tag (`fan_id`, `fan_flow_m3s`,
`fan_required_pa`, `fan_margin_pa`, `fan_power_w`). The catalog format is in
§5.4; the example fans are fictional.

### 3.14 `WENTAINSULATE` — insulation thickness per run

Select sized centrelines (runs without a size in their `WENTA` tag are
skipped; run `WENTASIZE` first). Prompts: purpose `[Condensation/HeatLoss]`,
material `[MineralWool/PeFoam/Epdm/Pir/PuFoam]`, the air temperature in the
duct, the ambient temperature and then either the ambient dew point
(condensation) or the heat-loss limit in W/m. For every round run it takes
the required thickness from `Insulation` (EN ISO 12241, §4.12), rounds it up
to the next standard step and reports the heat loss with that step. If the
bare duct already meets the purpose, it reports no insulation needed rather
than the smallest step. The result goes into the run's tag (`insul_mm`,
`insul_required_mm`, `insul_material`, `insul_purpose`,
`insul_heat_loss_w_per_m`) and into a label on layer `WENTA-INSUL`, for
example `INS 20 mm mineral_wool (condensation)`. Running it again updates
the labels in place. Rectangular runs are skipped with a message, because
the library's model is cylindrical. Insulation adds to the outer size of the
duct, which the 3D and clash work in the roadmap has to allow for.

### 3.15 `WENTASOUND` — room sound per terminal

Select the network. Prompts: space type
`[studio/bedroom/office/classroom/retail/industrial]`, the room surface
area (m²) and the mean absorption coefficient α. For every terminal it takes
the regenerated noise of the run feeding it and carries it into the room
with the room equation, then checks the result against the space type's
limit (`DrawingSound.Rooms`, §4.10). Rooms are listed worst first, the
values go into the terminal markers' tags (`sound_lp_db`, `sound_nc_limit`,
`sound_ok`), and the table is written to **`%TEMP%\wenta_sound.csv`**.
`WENTAATTEN` (§3.18) selects attenuators for the rooms over the limit.

### 3.16 `WENTAHELP` — command list and user guide

Prints every command with a one-line description and the paths of the
English and Polish user guides, which `install.ps1` copies next to the DLL.
The prompt `Open the user guide [English/Polski/No] <No>` opens the chosen
guide in the default web browser: an HTML page with a table of contents,
generated from the Markdown guide when the plugin is built
(`zwcad-plugin/tools/MakeHelp.cs`, `Wenta.Markdown`). If the HTML file is
missing it falls back to the Markdown file and the program Windows
associates with `.md`.

### 3.17 `WENTAWRITE` / `WENTAREAD` — network JSON ⇄ drawing

**`WENTAWRITE`** prompts for a network JSON file (default
`%TEMP%\wenta_read.json`; environment variables are expanded) and a base
point (default 0,0) and draws the network to scale: every duct is a
polyline on **`WENTA-DUCT`** with its length in millimetres, every other
component (source, tee, fitting, damper, attenuator, terminal) a 100 mm
circle on the same layer, and a label on **`WENTA-ANNO`**. Each entity
carries the full component record in its XData tag: `cid`, `type`, GUID,
name, `scope`, catalog entry and provenance, the section (with the exact
metres), the parameters (length, roughness, flow, ζ …) and its upstream
connections (`in.<port>` = `component:port`). When the JSON holds a
`layout_mm` / `at_mm` for every component (as `WENTAREAD` writes it) that
layout is used; otherwise the network is laid out from each source, ducts
along the current direction and the branch of each tee turned 90°. Running
`WENTAWRITE` again updates the existing entities in place (matched by GUID,
then by scope and id) instead of drawing a second copy.

**`WENTAREAD`** reads every tagged entity in model space back into a
network, checks it and writes the JSON in the design shape that the
retired Python `NetworkDesignSchema` also accepts (prompt for the path,
default `%TEMP%\wenta_read.json`), then solves it and prints ΔP. A duct
whose drawn length differs from its tag by more than 0.5 mm takes the
drawn length, and its tag and label are updated. The command **refuses**
(nothing is written) when the drawing has an orphaned tag (a component fed
by one that is no longer in the drawing), a missing or duplicated GUID (a
copied entity), a duplicated id, a class that does not match the type, a
duct record on a point entity or the reverse, a missing value, an unknown
port, or a network that does not validate. Up to 20 problems are listed.

### 3.18 `WENTAATTEN` — attenuators for rooms over their limit

Reads the drawn network the way `WENTAREAD` does (every tagged entity in
model space) and refuses, listing the problems, when the drawing does not
validate. Prompts: space type
`[studio/bedroom/office/classroom/retail/industrial]`, room surface area
(m²), mean absorption coefficient α, and the attenuator catalog (default
`example-attenuators.json` next to the DLL, §5.5). For every terminal it
computes the room sound as `WENTASOUND` does, from the rigid run that feeds
the terminal (through any two-port fittings) and minus any attenuator
already on that run. For each room over the limit it picks, from the
catalog entries of the feeding run's diameter (±0.5 mm), the one with the
lowest ζ whose insertion loss covers the excess (the shorter unit on a
tie), and splices it in front of the terminal as a two-port fitting
`atten_<terminal>` with the catalog's ζ, GUID, catalog entry and
provenance. The network is solved again and the new components are drawn
(500 mm back along the run's last segment) with `WENTAWRITE`, so
`WENTAREAD` reads them back. A room that no catalog entry can bring under
the limit is reported, not changed. The table goes to
**`%TEMP%\wenta_atten.csv`** and the updated network to
**`%TEMP%\wenta_atten.json`**.

The selection uses one single-number insertion loss per unit against the
single-number room level of §4.10; there are no octave-band data,
breakout or propagation along the ductwork, so treat it as a first check.
The units in `example-attenuators.json` are invented.

### 3.19 `WENTAPROPS` / `WENTAMOVE` / `WENTASTRETCH` — editing the drawn network

These three commands work on the network drawn by `WENTAWRITE` (§3.17).
Each reads every component record in model space as `WENTAREAD` does and
refuses, naming the first problem, when the drawing does not validate.
Each asks for a component id (Enter, then pick the entity, takes the id
from its tag), makes the edit in `Wenta.NetworkEdit`, solves the network
before and after, and redraws the records **in place**: the same
entities, GUIDs and XData, with the new lengths, sizes and labels, so
the identity of every component survives the edit and a following
`WENTAREAD` finds no length edits. The message gives the components
changed and the critical-path ΔP before and after.

- **`WENTAPROPS`** lists the component's properties (diameter or width
  and height in mm, length in m, flow in m³/s, ζ) and the flow it
  carries, then edits one of them. A diameter is snapped up to the next
  EN 1506 size (`StandardSizes.NearestRoundSize`, the message says so);
  widths and heights are taken as given; a flow is set on a terminal and
  propagates upstream; a length is a stretch (below). A property the
  component does not have (a length on a terminal, a flow on a duct) or a
  negative ζ is refused.
- **`WENTASTRETCH`** gives a duct a new length: the last vertex moves
  along the last segment and every component downstream moves by the
  same vector, so the drawing stays connected. A shortening that would
  remove the last segment is refused.
- **`WENTAMOVE`** moves a point component (tee, fitting, damper,
  attenuator, terminal, source) by a displacement in mm. The ends of the
  ducts joined to it follow, and those ducts take their new drawn length.
  Ducts themselves are not moved; stretch them or move the components at
  their ends.

### 3.20 `WENTAREFIT` — new terminal flow, re-size and re-balance

Prompts: the terminal, its new flow (m³/s), and the sizing method
`[Velocity/EqualFriction]` with its target. In one command it sets the
flow, opens every damper (a two-port fitting whose record has role
`damper`), re-sizes the affected legs (the ducts between the source and
that terminal) for their new flow with the `WENTASIZE` methods
(`ReFit.Apply`; the other runs carry the same flow and keep their size),
lets a tee or in-line fitting that had the section of the duct feeding it
take the duct's new section, solves the network, and
sets the damper on the run feeding each terminal to the ζ that brings the
terminal's path up to the critical one (`Balancing.BalancingZeta` at the
damper's own velocity; the damper on the critical path stays open). The
records are redrawn in place, so the new ζ is in each damper's tag. It
lists the re-sized ducts (old and new diameter and velocity), each
damper's ζ and opening, and any terminal with a surplus but no damper
(with the ζ a damper there would need). The message gives ΔP before and
after, the residual imbalance over the terminals that have a damper and
the time taken; the network is also saved to
**`%TEMP%\wenta_refit.json`**. `example-dampers-network.json`, next to
the DLL, is the reference network with a damper on each run; a bare file
name that `WENTAWRITE` does not find is looked up there.

### 3.21 `WENTASCHEDULE` / `WENTAFLAT` — schedule table and flat patterns

Both commands read the network drawn by `WENTAWRITE` (§3.17) as
`WENTAREAD` does, refuse a drawing that does not validate, and solve it.

- **`WENTASCHEDULE`** draws the schedule as a ZWCAD table on layer
  **`WENTA-SCHED`**: a title row, the header (Item, Branch, Kind,
  Description, Length [m], Area [m2], Flow [m3/s], KNR code), one row per
  component sorted by kind (source, ducts, flexible ducts, fittings,
  terminals) and then by id, and a totals row. The rows are `Wenta.Bom`'s
  (§3.3), with the branch number `Marking` gives each rigid duct, and the
  numbers are written with a decimal point whatever the Windows locale.
  The first run asks for the insertion point (top left); later runs find
  the table on `WENTA-SCHED` and **update it in place** at its position,
  so after an edit (`WENTAPROPS`, `WENTAREFIT`) one more `WENTASCHEDULE`
  brings it up to date. It also writes **`%TEMP%\wenta_schedule.xlsx`**,
  **`.json`** (the `BomExport` formats) and **`.csv`** (the table as
  drawn). Area is Bom's quantity (cross-section area × length, as in
  `WENTABOM`); the sheet area of the round ducts is `WENTAFLAT`'s.
- **`WENTAFLAT`** asks for a base point and the gap between pieces (mm,
  default 500) and draws the flat development of every round rigid duct
  on layer **`WENTA-FAB`** (cleared first): a closed rectangle of
  circumference × length (`Development.RoundDuctDevelopment`), laid out
  left to right in id order, labelled with the id, size and flat
  dimensions. Flexible and rectangular ducts are listed as skipped. The
  message gives the number of pieces and the sheet area; the pieces are
  also written to **`%TEMP%\wenta_flat.csv`**. Fittings (elbows, reducers)
  are not developed by this command; `Development` has their
  developments for use from code.

### 3.22 `WENTALABEL` — smart labels that follow edits

Labels every duct of the network drawn by `WENTAWRITE` (§3.17) with one
**`WENTA_LABEL`** block reference on `WENTA-ANNO`, below the longest
segment. The block has six attributes, `ID`, `BRANCH`, `SIZE`, `FLOW`,
`VEL` and `DPM` (id, branch number, size, flow in m³/s, velocity in m/s
and pressure drop in Pa/m from the solved network), so a block attribute
extract or a field can use them. Branch numbers are `Marking`'s: rigid
ducts are numbered breadth-first from the source by topology (ids in
order at each split), not by draw order, so a partial re-draw or an edit
does not renumber them. The duct's tag keeps the label's handle.

- **Auto-update.** Once a drawing has labels, `WENTAPROPS`,
  `WENTAMOVE`, `WENTASTRETCH`, `WENTAREFIT`, `WENTAATTEN`, a
  `WENTAWRITE` update and a `WENTAREAD` that takes edited lengths
  re-solve the network and refresh every label in place: new values and
  positions, missing labels added, labels of erased ducts removed. The
  message gives the number changed.
- **Style.** The prompt takes a label style JSON; Enter keeps the style
  stored in the drawing (the default the first time). The style is
  stored in the drawing and applied to every label in the same run, so
  changing it once restyles the whole drawing. Keys, all optional:
  `fields` (which attributes are visible and in what order: `id`,
  `branch`, `size`, `flow`, `velocity`, `dpm`), `text_height_mm` (100),
  `offset_mm` (−250, from the duct centreline), `flow_decimals` (3),
  `velocity_decimals` (2), `dpm_decimals` (2) and `schema_version` (1).
  Hidden fields stay in the block as invisible attributes.
  `example-label-style.json`, next to the DLL, shows id, size and flow
  at 150 mm; a bare file name is looked up there.
- The message gives the labels new, updated, changed and removed;
  `WENTALABEL` straight after an edit reports 0 changed.

### 3.23 Ribbon tab "Wenta"

One panel with twenty-four large buttons, each running one command:

| Button | Command |
|---|---|
| Duct Section | `WENTADUCT` |
| Wenta Panel | `WENTAPANEL` |
| Plugin Info | `WENTAHELLO` |
| Fitting Catalog | `WENTACATALOG` |
| BOM + KNR | `WENTABOM` |
| Tag Marker | `WENTATAG` |
| Trace Network | `WENTATRACE` |
| Pressure Drop | `WENTAPRESSURE` |
| Size Network | `WENTASIZE` |
| Balance Dampers | `WENTABALANCE` |
| Select Fan | `WENTAFAN` |
| Insulate | `WENTAINSULATE` |
| Room Sound | `WENTASOUND` |
| Attenuators | `WENTAATTEN` |
| Draw Network | `WENTAWRITE` |
| Read Network | `WENTAREAD` |
| Edit Properties | `WENTAPROPS` |
| Move Component | `WENTAMOVE` |
| Stretch Duct | `WENTASTRETCH` |
| Re-fit Flow | `WENTAREFIT` |
| Schedule Table | `WENTASCHEDULE` |
| Flat Patterns | `WENTAFLAT` |
| Smart Labels | `WENTALABEL` |
| Wenta Help | `WENTAHELP` |

### 3.24 Roadmap

Commands such as `WENTAFITTING`
are planned, not implemented; the library half of several of them already exists (see §4).
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
are assigned by traversal order, not geometry, unless
`TraceOptions.StraightByDirection` is set: then the straight leg is the
downstream run that best continues the incoming run's direction (the
network commands set it, §3.6). `TraceOptions.SourceAt` picks the open end
nearest a point as the source, and `TraceOptions.Sections` overrides a
run's section.

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

All four formats are UTF-8 JSON with an integer schema version; a reader
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

`NetworkJson.SerializeDesign` (used by `WENTAREAD`) writes the same network
in the design shape: `components` is an object keyed by id, each with
`type` first, and connections are `{ "source": "ahu:outlet", "target":
"duct:inlet" }`. `Parse` reads both shapes. Optional keys, written only when
set: `catalog_entry`, `provenance`, `role` (for a `TwoPortFitting`:
`damper`, `attenuator`, …), `layout_mm` (duct centreline `[[x, y], …]` in
drawing millimetres) and `at_mm` (`[x, y]` of a point component).

---

### 5.4 Fan catalog (`csharp/catalogs/example-fans.json`, version 1)

```json
{
  "name": "example-fans",
  "version": 1,
  "description": "...",
  "fans": [
    { "id": "ExampleFan EF-160", "efficiency": 0.55, "source": "fictional example",
      "points": [[0.0, 320.0], [0.1, 260.0], [0.2, 150.0], [0.3, 0.0]] }
  ]
}
```

Each fan has an `id` (unique in the file) and `points`, a list of
`[flow_m3s, static_pa]` pairs with strictly increasing flows (at least two).
`efficiency` (0 < η ≤ 1) is optional and only used for the power figure;
`source` and `notes` are free text. Order matters: `WENTAFAN` selects the
first fan that meets the duty point, so list fans from smallest to largest.
Load with `FanCatalog.Load(path)`. The four fans in `example-fans.json` are
invented and only exercise the format.

### 5.5 Attenuator catalog (`csharp/catalogs/example-attenuators.json`, version 1)

```json
{
  "name": "example-attenuators",
  "version": 1,
  "description": "...",
  "attenuators": [
    { "id": "ExampleAtten EA-160-600", "diameter_mm": 160, "length_mm": 600,
      "insertion_loss_db": 8.0, "zeta": 0.45, "source": "fictional example" }
  ]
}
```

Each attenuator has an `id` (unique in the file), the connection diameter
`diameter_mm` and `length_mm` (both positive), a single-number
`insertion_loss_db` (positive) and `zeta` (≥ 0, at the connection velocity).
`source` and `notes` are free text; `source` ends up in the inserted
component's provenance. A file with a `version` newer than 1 is refused.
Load with `AttenuatorCatalog.Load(path)`. The 21 units in
`example-attenuators.json` (Ø100–Ø315, 600/900/1200 mm) are invented and
only exercise the format.

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
  `%TEMP%\wenta_zwcad_test.txt`; `WENTABOM` writes `%TEMP%\wenta_bom.csv`,
  `WENTATRACE` `wenta_trace.json`, `WENTAPRESSURE` `wenta_pressure.csv` and
  `WENTABALANCE` `wenta_balance.csv` and `WENTASOUND` `wenta_sound.csv`,
  all in `%TEMP%`.
- **`open duct end(s) without a terminal flow`.** Every open end except the
  source needs a `WENTATAG` terminal marker within 150 mm; the message lists
  the positions.
- **Exceptions.** `ZwSoft.ZwCAD.Runtime.Exception` shadows `System.Exception`
  in plugin code; library errors are `Wenta.WentaException` with a message
  naming the offending field, file or component.
- **Known limitation.** `WENTABOM` solves a built-in reference network
  rather than the drawing; the drawn network is read by `WENTATRACE` and the
  other network commands (§3.6).

Report issues at <https://github.com/ModelTok/wenta/issues>. Include the
ZWCAD version printed by `WENTAHELLO`, the relevant lines of
`%TEMP%\wenta_zwcad_test.txt` and, for library problems, the
`WentaException` message.
