using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Wenta;

[assembly: CommandClass(typeof(WentaZwcad.DesignCommands))]

namespace WentaZwcad
{
    // Design checks on a drawn network: fan selection, insulation, room
    // sound, and the command overview. As with NetworkCommands, the
    // engineering is in Wenta.Core (FanSelection, DrawingInsulation,
    // DrawingSound); this class prompts, reads the selection and writes back.
    public class DesignCommands
    {
        private const double TextHeightMm = 80.0;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static string F(double v, string fmt) { return v.ToString(fmt, C); }

        private static string PluginDir()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        // ---------------------------------------------------------------- WENTAFAN

        [CommandMethod("WENTAFAN")]
        public void FanCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers: ");
            if (sel == null) return;
            Fluid air = CadIo.PromptFluid(ed);
            if (air == null) return;
            double? extra = CadIo.PromptDouble(ed, "\nExtra pressure outside the ductwork (AHU, filters, coils) [Pa] <0>: ",
                0.0, true);
            if (extra == null) return;
            string def = Path.Combine(PluginDir(), "example-fans.json");
            var so = new PromptStringOptions("\nFan catalog JSON <" + def + ">: ") { AllowSpaces = true };
            PromptResult pr = ed.GetString(so);
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
            string path = string.IsNullOrWhiteSpace(pr.StringResult) ? def : pr.StringResult.Trim().Trim('"');

            try
            {
                if (!File.Exists(path)) throw new WentaException("fan catalog not found: " + path);
                FanCatalog cat = FanCatalog.Load(path);
                DrawingTrace t = CadIo.Trace(ed, sel);
                double flow, required;
                FanSelection.DutyPoint(t, air, extra.Value, out flow, out required);
                List<FanCandidate> cands = FanSelection.Evaluate(cat, flow, required);
                int? pick = FanSelection.Pick(cat, flow, required);

                ed.WriteMessage(string.Format(
                    "\nDuty point {0} m³/s at {1} Pa ({2} Pa ductwork + {3} Pa extra, air ρ={4} kg/m³). Catalog '{5}', {6} fans:",
                    F(flow, "0.000"), F(required, "0.0"), F(required - extra.Value, "0.00"), F(extra.Value, "0.0"),
                    F(air.Density, "0.000"), cat.Name ?? Path.GetFileName(path), cat.Fans.Count));
                for (int i = 0; i < cands.Count; i++)
                {
                    FanCandidate c = cands[i];
                    string state = !c.MarginPa.HasValue ? "outside the curve"
                        : string.Format("{0} Pa available, margin {1} Pa{2}", F(c.AvailablePa.Value, "0.0"),
                            F(c.MarginPa.Value, "0.0"), c.Meets ? "" : " (too weak)");
                    string power = c.PowerW.HasValue ? string.Format(", power {0} W", F(c.PowerW.Value, "0.0")) : "";
                    ed.WriteMessage(string.Format("\n  {0} {1}: {2}{3}", pick == i ? "*" : " ", c.Fan.Id, state, power));
                }
                if (pick == null)
                {
                    ed.WriteMessage("\nNo fan in the catalog meets the duty point.");
                    Commands.Log(string.Format(C, "WENTAFAN ok  none  {0:R} m3/s  {1:R} Pa", flow, required));
                    return;
                }
                FanCandidate chosen = cands[pick.Value];
                ed.WriteMessage("\nSelected: " + chosen.Fan.Id + " (first fan in the catalog that meets the duty point).");

                DrawnMarker src = sel.Markers.Find(m => m.Class == WentaTag.ClassSource);
                if (src != null)
                {
                    Database db = doc.Database;
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        WentaTag tag = CadIo.TagFor(sel, src.Handle, src.Class);
                        tag.Set("fan_id", chosen.Fan.Id);
                        tag.Set("fan_flow_m3s", flow);
                        tag.Set("fan_required_pa", required);
                        tag.Set("fan_margin_pa", chosen.MarginPa.Value);
                        if (chosen.PowerW.HasValue) tag.Set("fan_power_w", chosen.PowerW.Value);
                        Entity e = (Entity)tr.GetObject(sel.Ids[src.Handle], OpenMode.ForWrite);
                        CadIo.WriteTag(tr, db, e, tag);
                        tr.Commit();
                    }
                    ed.WriteMessage(" Written to the source marker.");
                }
                Commands.Log(string.Format(C, "WENTAFAN ok  {0}  {1:R} m3/s  {2:R} Pa  margin {3:0.0000}  power {4}",
                    chosen.Fan.Id, flow, required, chosen.MarginPa.Value,
                    chosen.PowerW.HasValue ? chosen.PowerW.Value.ToString("0.0000", C) : "n/a"));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAFAN: " + ex.Message);
                Commands.Log("WENTAFAN FAILED " + ex.Message);
            }
        }

        // ----------------------------------------------------------- WENTAINSULATE

        private static readonly string[] MaterialKeys = { "MineralWool", "PeFoam", "Epdm", "Pir", "PuFoam" };
        private static readonly string[] MaterialNames = { "mineral_wool", "pe_foam", "epdm_nbr", "pir", "pu_foam" };

        [CommandMethod("WENTAINSULATE")]
        public void Insulate()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect sized duct centrelines to insulate: ");
            if (sel == null) return;

            string purpose = CadIo.PromptKeyword(ed, "\nPurpose [Condensation/HeatLoss] <Condensation>: ",
                "Condensation", "Condensation", "HeatLoss");
            if (purpose == null) return;
            string mk = CadIo.PromptKeyword(ed, "\nMaterial [MineralWool/PeFoam/Epdm/Pir/PuFoam] <MineralWool>: ",
                "MineralWool", MaterialKeys);
            if (mk == null) return;
            string material = MaterialNames[Array.IndexOf(MaterialKeys, mk)];
            double? air = CadIo.PromptDouble(ed, "\nAir temperature in the duct [°C] <16>: ", 16.0, true, true);
            if (air == null) return;
            double? amb = CadIo.PromptDouble(ed, "\nAmbient temperature [°C] <24>: ", 24.0, true, true);
            if (amb == null) return;
            double dew = 0.0, target = 0.0;
            string key = DrawingInsulation.Condensation;
            if (purpose == "Condensation")
            {
                double? v = CadIo.PromptDouble(ed, "\nAmbient dew point [°C] <15>: ", 15.0, true, true);
                if (v == null) return;
                dew = v.Value;
            }
            else
            {
                key = DrawingInsulation.HeatLoss;
                double? v = CadIo.PromptDouble(ed, "\nHeat-loss limit [W/m] <20>: ", 20.0);
                if (v == null) return;
                target = v.Value;
            }

            Database db = doc.Database;
            int done = 0, skipped = 0, labelsNew = 0, labelsUpdated = 0;
            var thick = new List<string>();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadIo.EnsureLayer(tr, db, CadIo.LayerInsul, 4);
                foreach (DrawnDuct d in sel.Ducts)
                {
                    if (d.Section == null)
                    {
                        skipped++;
                        ed.WriteMessage("\n  " + d.Handle + ": no size in its WENTA tag (run WENTASIZE first), skipped");
                        continue;
                    }
                    InsulationSizing s;
                    try
                    {
                        s = DrawingInsulation.Size(d.Section, key, material, air.Value, amb.Value, dew, target);
                    }
                    catch (WentaException ex)
                    {
                        skipped++;
                        ed.WriteMessage("\n  " + d.Handle + ": " + ex.Message);
                        continue;
                    }
                    WentaTag tag = CadIo.TagFor(sel, d.Handle, WentaTag.ClassDuct);
                    tag.Set("insul_purpose", key);
                    tag.Set("insul_material", material);
                    tag.Set("insul_required_mm", s.RequiredM * 1000.0);
                    tag.Set("insul_mm", s.SelectedM * 1000.0);
                    tag.Set("insul_heat_loss_w_per_m", s.HeatLossWPerM);
                    string label = s.SelectedM > 0.0
                        ? string.Format("INS {0} mm {1} ({2})", F(s.SelectedM * 1000.0, "0"), material, key)
                        : string.Format("INS none needed ({0})", key);
                    string before = tag.Get("insul_h");
                    CadIo.UpsertText(tr, db, tag, "insul_h", CadIo.LayerInsul, CadIo.LabelPoint(d, -140.0),
                        TextHeightMm, label);
                    if (before != null && before == tag.Get("insul_h")) labelsUpdated++; else labelsNew++;
                    Entity e = (Entity)tr.GetObject(sel.Ids[d.Handle], OpenMode.ForWrite);
                    CadIo.WriteTag(tr, db, e, tag);
                    done++;
                    thick.Add(DrawingSizing.SizeText(d.Section) + "=" + F(s.SelectedM * 1000.0, "0"));
                    ed.WriteMessage(string.Format("\n  {0}  {1}: required {2} mm → {3} mm, heat loss {4} W/m",
                        d.Handle, DrawingSizing.SizeText(d.Section), F(s.RequiredM * 1000.0, "0.0"),
                        F(s.SelectedM * 1000.0, "0"), F(s.HeatLossWPerM, "0.00")));
                }
                tr.Commit();
            }
            ed.WriteMessage(string.Format("\nInsulated {0} runs ({1}, {2}); {3} skipped. Labels on {4}: {5} new, {6} updated.",
                done, key, material, skipped, CadIo.LayerInsul, labelsNew, labelsUpdated));
            Commands.Log(string.Format(C, "WENTAINSULATE ok  {0} {1}  {2}  skipped {3}  labels new {4} updated {5}",
                key, material, string.Join(" ", thick.ToArray()), skipped, labelsNew, labelsUpdated));
        }

        // -------------------------------------------------------------- WENTASOUND

        [CommandMethod("WENTASOUND")]
        public void SoundCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers: ");
            if (sel == null) return;
            string space = CadIo.PromptKeyword(ed,
                "\nSpace type [studio/bedroom/office/classroom/retail/industrial] <office>: ", "office",
                "studio", "bedroom", "office", "classroom", "retail", "industrial");
            if (space == null) return;
            double? area = CadIo.PromptDouble(ed, "\nRoom surface area [m²] <60>: ", 60.0);
            if (area == null) return;
            double? alpha = CadIo.PromptDouble(ed, "\nMean absorption coefficient α (0..1) <0.2>: ", 0.2);
            if (alpha == null) return;

            try
            {
                DrawingTrace t = CadIo.Trace(ed, sel);
                List<RoomSound> rooms = DrawingSound.Rooms(t, space, area.Value, alpha.Value);
                var csv = new StringBuilder("terminal,run,velocity_ms,diameter_m,sound_power_db,pressure_level_db,nc_limit,ok\n");
                Database db = doc.Database;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (RoomSound r in rooms)
                    {
                        ed.WriteMessage(string.Format("\n  {0,-6} via {1,-6} {2} m/s  Lw {3} dB  Lp {4} dB  limit NC {5}  {6}",
                            r.TerminalId, r.ChainId, F(r.VelocityMs, "0.00"), F(r.SoundPowerDb, "0.0"),
                            F(r.PressureLevelDb, "0.0"), F(r.NcLimit, "0"), r.Ok ? "ok" : "TOO LOUD"));
                        csv.Append(string.Join(",", new[]
                        {
                            r.TerminalId, r.ChainId, r.VelocityMs.ToString("R", C), r.DiameterM.ToString("R", C),
                            r.SoundPowerDb.ToString("R", C), r.PressureLevelDb.ToString("R", C),
                            r.NcLimit.ToString("R", C), r.Ok ? "1" : "0",
                        })).Append('\n');
                        string marker;
                        if (t.TerminalMarker.TryGetValue(r.TerminalId, out marker) && marker != null
                            && sel.Ids.ContainsKey(marker))
                        {
                            WentaTag tag = CadIo.TagFor(sel, marker, WentaTag.ClassTerminal);
                            tag.Set("sound_space", space);
                            tag.Set("sound_lp_db", r.PressureLevelDb);
                            tag.Set("sound_nc_limit", r.NcLimit);
                            tag.Set("sound_ok", r.Ok ? "1" : "0");
                            Entity e = (Entity)tr.GetObject(sel.Ids[marker], OpenMode.ForWrite);
                            CadIo.WriteTag(tr, db, e, tag);
                        }
                    }
                    tr.Commit();
                }
                string file = Path.Combine(Path.GetTempPath(), "wenta_sound.csv");
                File.WriteAllText(file, csv.ToString());
                if (rooms.Count > 0)
                {
                    RoomSound w = rooms[0];
                    int loud = rooms.FindAll(r => !r.Ok).Count;
                    ed.WriteMessage(string.Format(
                        "\nWorst room: {0} at {1} dB against NC {2} ({3}); {4} of {5} terminals over the limit. CSV: {6}",
                        w.TerminalId, F(w.PressureLevelDb, "0.0"), F(w.NcLimit, "0"), space, loud, rooms.Count, file));
                    Commands.Log(string.Format(C, "WENTASOUND ok  {0}  worst {1} {2:R} dB  limit {3:0}  over {4}",
                        space, w.TerminalId, w.PressureLevelDb, w.NcLimit, loud));
                }
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTASOUND: " + ex.Message);
                Commands.Log("WENTASOUND FAILED " + ex.Message);
            }
        }

        // --------------------------------------------------------------- WENTAHELP

        private static readonly string[][] CommandList =
        {
            new[] { "WENTADUCT", "size and draw one duct section" },
            new[] { "WENTAPANEL", "dockable sizing palette with live preview" },
            new[] { "WENTACATALOG", "load the open zeta catalog" },
            new[] { "WENTABOM", "BOM + KNR rows of the reference network" },
            new[] { "WENTATAG", "place or edit a terminal, source or damper marker" },
            new[] { "WENTATRACE", "build the network from centrelines and markers" },
            new[] { "WENTAPRESSURE", "critical-path pressure drop and WENTA-CRIT overlay" },
            new[] { "WENTASIZE", "size every run, labels updated in place" },
            new[] { "WENTABALANCE", "damper zeta and opening per terminal" },
            new[] { "WENTAFAN", "pick a fan from a fan-curve catalog at the duty point" },
            new[] { "WENTAINSULATE", "EN ISO 12241 insulation thickness for round runs" },
            new[] { "WENTASOUND", "regenerated noise per room against the NC limit" },
            new[] { "WENTAWRITE", "draw a network JSON to scale, re-writes update in place" },
            new[] { "WENTAREAD", "read the drawn network back to JSON, refusing bad tags" },
            new[] { "WENTAATTEN", "attenuator per room over its NC limit, inserted and solved" },
            new[] { "WENTAPROPS", "show and edit a drawn component's size, length, flow or zeta" },
            new[] { "WENTAMOVE", "move a drawn tee, fitting or terminal; joined ducts follow" },
            new[] { "WENTASTRETCH", "new length for a drawn duct; downstream moves with it" },
            new[] { "WENTAREFIT", "new terminal flow: re-size, re-solve and re-set dampers" },
            new[] { "WENTASCHEDULE", "schedule table of the drawn network; XLSX, JSON and CSV" },
            new[] { "WENTAFLAT", "flat developments of the round ducts for fabrication" },
            new[] { "WENTALABEL", "smart duct labels (size, flow, velocity, Pa/m); follow edits" },
            new[] { "WENTAHELLO", "version banner" },
            new[] { "WENTAHELP", "this list and the user guide" },
        };

        [CommandMethod("WENTAHELP")]
        public void Help()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ed.WriteMessage("\nWenta commands:");
            foreach (string[] c in CommandList)
                ed.WriteMessage(string.Format("\n  {0,-14} {1}", c[0], c[1]));
            // The HTML guides are generated from docs/user-guide.*.md by
            // build.cmd (tools/MakeHelp.cs); the Markdown is the fallback.
            string en = Guide("en");
            string pl = Guide("pl");
            ed.WriteMessage("\nUser guide: " + en + "\nPodręcznik: " + pl);
            string open = CadIo.PromptKeyword(ed, "\nOpen the user guide [English/Polski/No] <No>: ", "No",
                "English", "Polski", "No");
            if (open == "English" || open == "Polski")
            {
                string path = open == "English" ? en : pl;
                try
                {
                    if (!File.Exists(path)) throw new FileNotFoundException(path);
                    System.Diagnostics.Process.Start(path);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nCould not open " + path + " (" + ex.Message + ")");
                }
            }
            Commands.Log(string.Format(C, "WENTAHELP ok  {0} commands  guide {1}", CommandList.Length,
                !File.Exists(en) ? "missing" : en.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "html" : "md"));
        }

        private static string Guide(string lang)
        {
            string html = Path.Combine(PluginDir(), "user-guide." + lang + ".html");
            return File.Exists(html) ? html : Path.Combine(PluginDir(), "user-guide." + lang + ".md");
        }
    }
}
