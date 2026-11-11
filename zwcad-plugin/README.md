# WentaZwcad — ZWCAD 2021 duct plugin (wenta C# core)

A ductwork design plugin for ZWCAD 2021, powered by **Wenta.Core** — the
C# port of the `wenta` library (see `../csharp/README.md`). The whole math
core is compiled into the plugin DLL: single-file deployment, no Python,
Mojo, Rust or WASM at runtime.

## Commands

| Command | What it does |
|---|---|
| `WENTADUCT` | Prompts shape (Rect/Round) + flow + target velocity → sizes via `Sizing.VelocityMethod` (EN 1505/1506), draws the section + label (size · flow · v · Pa/m), stores the design record in **XData** (app `WENTA`) |
| `WENTACATALOG` | Loads the open ζ-catalog (`example-generic.json`) and demos a lookup with provenance |
| `WENTABOM` | Solves the reference tee network (critical path 7.63 Pa — parity-vector value) and exports the BOM + KNR rows to `%TEMP%\wenta_bom.csv` |
| `WENTAPANEL` | Opens the dockable sizing palette: flow/velocity/shape in, live section preview, one-click `WENTADUCT` |
| `WENTAHELLO` | Version/info banner + evidence log |
| `WENTATAG` | Places or edits a terminal (with design flow), source or damper marker: circle on `WENTA-TAG` + label, v2 `WENTA` tag with a GUID |
| `WENTATRACE` | Builds the network from the drawn centrelines (LINE / LWPOLYLINE / ARC) and markers via `DrawingTrace`, prints runs/tees/terminals and the critical-path ΔP, saves `%TEMP%\wenta_trace.json` |
| `WENTAPRESSURE` | Critical-path `PressureReport` + per-component table at a site altitude, `%TEMP%\wenta_pressure.csv`, `WENTA-CRIT` overlay, change since the previous run |
| `WENTASIZE` | Sizes every run (Velocity / EqualFriction / Budget / Noise / Aspect, round or rectangular) via `DrawingSizing`, writes the tag and updates the `WENTA-LABEL` labels in place |
| `WENTABALANCE` | Damper ζ / opening per terminal (`ReFit.Hints`, `DrawingBalancing`), written to the markers and their labels, `CHANGE HARDWARE` flag, `%TEMP%\wenta_balance.csv` |
| `WENTAFAN` | Duty point (terminal flow, critical ΔP + extra) against a fan-curve catalog JSON (`example-fans.json`); lists every fan with margin and power, picks the first that meets it (`Fan.PickFan`), writes it to the source marker |
| `WENTAINSULATE` | EN ISO 12241 thickness (condensation or heat-loss limit) for each sized round run via `DrawingInsulation`, snapped to the standard step; tag + label on `WENTA-INSUL`, updated in place |
| `WENTASOUND` | Regenerated noise of the run feeding each terminal through the room equation against the space type's NC limit (`DrawingSound`); worst room first, terminal tags, `%TEMP%\wenta_sound.csv` |
| `WENTAATTEN` | For every room over its limit, picks the lowest-ζ attenuator of the feeding run's diameter that covers the excess from a catalog JSON (`example-attenuators.json`, fictional), splices it in front of the terminal, re-solves and draws it (`AttenuatorSelection`); `%TEMP%\wenta_atten.csv` / `.json` |
| `WENTAWRITE` | Draws a network JSON to scale (ducts as polylines on `WENTA-DUCT`, other components as circles, labels on `WENTA-ANNO`), each entity tagged with its full component record (`NetworkDrawing`); a second run updates the entities in place |
| `WENTAREAD` | Reads the tagged entities back to a network JSON (design shape), taking edited drawn lengths; refuses orphaned tags, copied GUIDs, class mismatches and missing values |
| `WENTAPROPS` | Lists one drawn component's size, length, flow and ζ and edits one of them (`NetworkEdit.Set`: diameter snapped up to EN 1506, flow on terminals); re-solves and redraws the records in place, GUIDs and XData kept |
| `WENTAMOVE` | Moves a drawn tee, fitting, damper or terminal by a displacement; the joined ducts' ends follow and take the drawn length (`NetworkEdit.Move`) |
| `WENTASTRETCH` | New length for a drawn duct; its end and everything downstream move with it (`NetworkEdit.Stretch`) |
| `WENTAREFIT` | New flow at one terminal: re-sizes the legs feeding it (`ReFit.Apply`), re-solves and sets each `damper`-role fitting to balance its terminal (`NetworkEdit.ReFlow`), all in one command; `%TEMP%\wenta_refit.json` |
| `WENTASCHEDULE` | Schedule table of the drawn network on `WENTA-SCHED` (Bom rows with branch numbers, sorted, totals; `Schedule.Build`), updated in place on a re-run; `%TEMP%\wenta_schedule.xlsx`, `.json`, `.csv` |
| `WENTAFLAT` | Flat developments of the round rigid ducts (circumference × length, `Development`) as closed outlines with labels on `WENTA-FAB`; `%TEMP%\wenta_flat.csv` |
| `WENTALABEL` | Smart labels: one `WENTA_LABEL` attributed block per drawn duct on `WENTA-ANNO` (id, branch no., size, flow, velocity, Pa/m; `Annotation.Build`), style JSON stored in the drawing and applied drawing-wide; the editing commands refresh them after every edit |
| `WENTAHELP` | Command list and the paths of the EN/PL user guides; optionally opens one as HTML (built from the Markdown by `tools/MakeHelp.cs`) |

## UI

- **Ribbon tab "Wenta"** — partial CUIX (`Wenta.CUIX`, built by
  `tools/MakeCuix.cs`, modeled 1:1 on ZWSOFT's own `APP+.cuix` package format).
  Buttons: Duct Section · Wenta Panel · Plugin Info · Fitting Catalog ·
  BOM + KNR · Tag Marker · Trace Network · Pressure Drop · Size Network ·
  Balance Dampers · Select Fan · Insulate · Room Sound · Attenuators · Draw Network ·
  Read Network · Wenta Help. Load once with `MENULOAD`; verified live in the ZWCAD UI tree
  (`Wenta [ControlType.TabItem]`, see `uia-check.ps1`).
- **Palette** — no longer auto-opens at startup (user preference: ribbon);
  `WENTAPANEL` / ribbon button opens it on demand.

## Build → install → verify

```cmd
just csharp-parity            # 857 assertions: frozen vectors + closed-form module tests
just zwcad-build              # WentaZwcad.dll (core compiled in) + CUIX
powershell -ExecutionPolicy Bypass -File install.ps1   # elevated
```

Headless end-to-end (`full_test.scr`): auto-loads from the registry,
`MENULOAD`s the CUIX, runs every command, saves the DWG. Verified output:

```
plugin loaded (wenta C# core)
WENTADUCT ok  200×200 mm  0,150 m3/s  3,75 m/s  0,9530 Pa/m
WENTACATALOG ok  5 entries
WENTABOM ok  7 rows  7,63 Pa
WENTAHELLO ok  ZWCAD 21.10.21.0
```

Network commands (`network_test.scr`, plugin loaded first): draws a 5 m
main, a tee, a 3 m straight run and a 3 m branch, tags them and runs every
network command. Verified on ZWCAD 2021: trace 4.43 Pa at the default Ø200,
sized (velocity 4 m/s → Ø200 / Ø150 / Ø125) 8.00 Pa, a second `WENTASIZE`
updates the three labels in place, balance ζ 0.15 / 93 % open on the
straight-run terminal; then `WENTAFAN` with 150 Pa extra picks
`ExampleFan EF-160` (0.12 m³/s, 160.7 Pa), `WENTAINSULATE` gives 20 mm on
every run and updates its labels on the second run, `WENTASOUND` and
`WENTAHELP` run, and the `WENTAWRITE`/`WENTAREAD` round trip reproduces
4.43 Pa with byte-identical JSON on the second read, refuses orphaned tags
and picks up an edited duct length (5.14 Pa); last, `WENTAATTEN` (studio)
inserts and draws two attenuators, no room stays over the limit, and ΔP
goes from 5.14 to 6.84 Pa, which `WENTAREAD` reads back; then `WENTAPROPS` (Ø270 → Ø300),
`WENTASTRETCH`, `WENTAMOVE` and a terminal flow edit take it to 7.22 Pa and `WENTAREAD` finds no
length edits and the same GUIDs; on `example-dampers-network.json`, `WENTAREFIT` (t1 0.2 m³/s)
re-sizes the main and straight run to Ø300, sets damper `dS` to ζ 0.05 and gives 3.70 Pa, balanced, in about 6 ms;
`WENTASCHEDULE` draws the 12-row table, a flow edit and a second `WENTASCHEDULE` update it in place, and
`WENTAFLAT` draws three flat pieces (9.048 m²); `WENTALABEL` labels the ducts, an edit refreshes them and a
re-run finds 0 stale, and a style file restyles all three in one pass —
the values `Wenta.Core` computes for the layout.

## ZWCAD platform facts (hard-won, keep)

- Managed API = `ZwManaged.dll` (acmgd) + `ZwDatabaseMgd.dll` (acdbmgd),
  mixed-mode **x64** → compile `/platform:x64`.
- Install = HKLM registry demand-loading only (`LOADCTRLS=14`; HKCU is
  ignored; UAC required). `ZwSoft.ZwCAD.Runtime.Exception` shadows
  `System.Exception`.
- `SendStringToExecute(cmd, activate, wrapUpInactiveDoc, echoCommand)`.
- NETLOAD-in-script needs `FILEDIA 0`; MENULOAD likewise.
- Ribbon = CUIX zip-of-XML (schema in APP+/ZWCAD.CUIX); tab needs
  `DefaultDisplay="AddToWorkSpace" WorkspaceBehavior="MergeOrAddTab"`.
- Headless evidence: log lines to `%TEMP%\wenta_zwcad_test.txt`; UI
  verification via `uia-check.ps1` (UIA tree) — screenshot reading is not
  always available.

## Repo map

```
WentaZwcad/        plugin sources (Commands, Plugin, WentaPanel, NetworkCommands, DesignCommands, CadIo)
../csharp/         Wenta.Core + test suite (frozen vectors) + catalogs
tools/MakeCuix.cs  CUIX ribbon package builder
build.cmd install.ps1 uninstall.ps1   build & deploy
load_test.scr full_test.scr autoload_test.scr network_test.scr   headless test scripts
uia-check.ps1      UI-tree verification (ribbon tab presence)
ROADMAP.md         the plan (C#-first, UX phase, PL-market items)
```
