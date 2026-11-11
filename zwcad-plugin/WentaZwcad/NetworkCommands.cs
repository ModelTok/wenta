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

[assembly: CommandClass(typeof(WentaZwcad.NetworkCommands))]

namespace WentaZwcad
{
    // Commands that work on a drawn duct network: centrelines (LINE,
    // LWPOLYLINE, ARC) plus WENTATAG markers. The engineering is all in
    // Wenta.Core (DrawingTrace, PressureReport, DrawingSizing, ReFit);
    // this class only prompts, reads the selection and writes results back.
    public class NetworkCommands
    {
        private const double MarkerRadiusMm = 100.0;
        private const double TextHeightMm = 80.0;
        private const string LastDpKey = "WENTA_LAST_DP";
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static string F(double v, string fmt) { return v.ToString(fmt, C); }

        // ---------------------------------------------------------------- WENTATAG

        [CommandMethod("WENTATAG")]
        public void Tag()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string kind = CadIo.PromptKeyword(ed, "\nMarker [Terminal/Source/Damper] <Terminal>: ",
                "Terminal", "Terminal", "Source", "Damper");
            if (kind == null) return;
            string cls = kind == "Source" ? WentaTag.ClassSource
                : kind == "Damper" ? WentaTag.ClassDamper : WentaTag.ClassTerminal;

            PromptPointResult pr = ed.GetPoint(new PromptPointOptions("\n" + kind + " position (on the duct end or run): "));
            if (pr.Status != PromptStatus.OK) return;
            Point3d at = new Point3d(pr.Value.X, pr.Value.Y, 0.0);

            double flow = 0.0;
            if (cls == WentaTag.ClassTerminal)
            {
                double? f = CadIo.PromptDouble(ed, "\nTerminal design flow [m³/s] <0.1>: ", 0.1);
                if (f == null) return;
                flow = f.Value;
            }

            Database db = doc.Database;
            bool updated;
            string handle;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadIo.EnsureLayer(tr, db, CadIo.LayerTag, 3);
                Circle marker = FindMarker(tr, db, at, out updated);
                WentaTag tag;
                if (marker == null)
                {
                    marker = new Circle(at, Vector3d.ZAxis, MarkerRadiusMm) { Layer = CadIo.LayerTag };
                    CadIo.ModelSpace(tr, db).AppendEntity(marker);
                    tr.AddNewlyCreatedDBObject(marker, true);
                    tag = WentaTag.New(cls);
                }
                else
                {
                    marker.UpgradeOpen();
                    tag = CadIo.ReadTag(marker) ?? WentaTag.New(cls);
                    if (string.IsNullOrEmpty(tag.Guid)) tag.Guid = Guid.NewGuid().ToString("D");
                    tag.Class = cls;
                    marker.Center = at;
                }
                if (cls == WentaTag.ClassTerminal) tag.Set("flow_m3s", flow);
                else tag.Set("flow_m3s", (string)null);
                // A changed duty invalidates the last balancing result.
                foreach (string k in new[] { "balance_zeta", "balance_open_pct", "balance_surplus_pa", "needs_hardware" })
                    tag.Set(k, (string)null);
                CadIo.UpsertText(tr, db, tag, "label_h", CadIo.LayerTag,
                    new Point3d(at.X + MarkerRadiusMm * 1.2, at.Y + MarkerRadiusMm * 1.2, 0.0),
                    TextHeightMm, MarkerText(tag));
                CadIo.WriteTag(tr, db, marker, tag);
                handle = marker.Handle.ToString();
                tr.Commit();
            }

            string what = cls == WentaTag.ClassTerminal ? kind + " " + F(flow, "0.000") + " m³/s" : kind;
            ed.WriteMessage(string.Format("\n{0} {1} at ({2}, {3}).", updated ? "Updated" : "Tagged",
                what, F(at.X, "0"), F(at.Y, "0")));
            Commands.Log(string.Format(C, "WENTATAG ok  {0} {1}  at {2:0},{3:0}  {4}",
                cls, updated ? "updated" : "new", at.X, at.Y,
                cls == WentaTag.ClassTerminal ? F(flow, "0.000") + " m3/s" : "-"));
        }

        // An existing WENTA marker within the snap radius of the point, so
        // re-tagging edits it instead of stacking a duplicate.
        private static Circle FindMarker(Transaction tr, Database db, Point3d at, out bool found)
        {
            found = false;
            BlockTableRecord ms = CadIo.ModelSpace(tr, db);
            Circle best = null;
            double bestD = MarkerRadiusMm * 1.5;
            foreach (ObjectId id in ms)
            {
                Circle c = tr.GetObject(id, OpenMode.ForRead) as Circle;
                if (c == null) continue;
                WentaTag t = CadIo.ReadTag(c);
                if (t == null || t.Legacy) continue;
                if (t.Class != WentaTag.ClassTerminal && t.Class != WentaTag.ClassSource && t.Class != WentaTag.ClassDamper)
                    continue;
                double d = c.Center.DistanceTo(at);
                if (d <= bestD) { bestD = d; best = c; }
            }
            found = best != null;
            return best;
        }

        private static string MarkerText(WentaTag tag)
        {
            var sb = new StringBuilder();
            if (tag.Class == WentaTag.ClassSource) sb.Append("SRC");
            else if (tag.Class == WentaTag.ClassDamper) sb.Append("DMP");
            else sb.Append("T ").Append(F(tag.GetDouble("flow_m3s") ?? 0.0, "0.000")).Append(" m³/s");
            double? zeta = tag.GetDouble("balance_zeta"), open = tag.GetDouble("balance_open_pct");
            if (zeta.HasValue && open.HasValue)
                sb.Append("  ζ=").Append(F(zeta.Value, "0.00")).Append(" ").Append(F(open.Value, "0")).Append("% open");
            if (tag.Get("needs_hardware") == "1") sb.Append("  CHANGE HARDWARE");
            return sb.ToString();
        }

        // -------------------------------------------------------------- WENTATRACE

        [CommandMethod("WENTATRACE")]
        public void TraceCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers: ");
            if (sel == null) return;
            try
            {
                DrawingTrace t = CadIo.Trace(ed, sel);
                double dp = t.Network.Solve();
                int tees = 0, terminals = 0;
                foreach (Component c in t.Network.Components.Values)
                {
                    if (c is Tee) tees++;
                    else if (c is Terminal) terminals++;
                }
                double total = 0.0;
                foreach (Component c in t.Network.Terminals()) total += ((Terminal)c).Flowrate;

                string json = Path.Combine(Path.GetTempPath(), "wenta_trace.json");
                NetworkJson.Save(t.Network, json, Meta(t, sel));

                ed.WriteMessage(string.Format(
                    "\nTraced {0} duct runs ({1} entities), {2} tees, {3} terminals; {4} m of duct, {5} m³/s at the source." +
                    "\nCritical-path ΔP {6} Pa. Network JSON: {7}",
                    t.System.Chains.Count, sel.Ducts.Count, tees, terminals, F(t.System.TotalLengthM(), "0.00"),
                    F(total, "0.000"), F(dp, "0.00"), json));
                foreach (Chain ch in t.System.Chains)
                {
                    Component c = t.Network.Components[ch.Id];
                    ed.WriteMessage(string.Format("\n  {0}  {1} m  {2}  {3} m³/s", ch.Id, F(ch.LengthM, "0.00"),
                        DrawingSizing.SizeText(((RigidDuct)c).CrossSection), F(c.InletFlowrate() ?? 0.0, "0.000")));
                }
                Commands.Log(string.Format(C, "WENTATRACE ok  {0} runs  {1} tees  {2} terminals  {3:0.000} m  {4:R} Pa",
                    t.System.Chains.Count, tees, terminals, t.System.TotalLengthM(), dp));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTATRACE: " + ex.Message);
                Commands.Log("WENTATRACE FAILED " + ex.Message);
            }
        }

        // Ties each network component to the GUID of the entity it came
        // from, so the saved JSON can be matched back to the drawing.
        private static Dictionary<string, ComponentMeta> Meta(DrawingTrace t, CadIo.Selection sel)
        {
            var meta = new Dictionary<string, ComponentMeta>();
            foreach (KeyValuePair<string, List<string>> kv in t.ChainHandles)
            {
                WentaTag tag;
                if (kv.Value.Count > 0 && sel.Tags.TryGetValue(kv.Value[0], out tag) && !string.IsNullOrEmpty(tag.Guid))
                    meta[kv.Key] = new ComponentMeta { Guid = tag.Guid };
            }
            foreach (KeyValuePair<string, string> kv in t.TerminalMarker)
            {
                WentaTag tag;
                if (kv.Value != null && sel.Tags.TryGetValue(kv.Value, out tag) && !string.IsNullOrEmpty(tag.Guid))
                    meta[kv.Key] = new ComponentMeta { Guid = tag.Guid };
            }
            return meta;
        }

        // ----------------------------------------------------------- WENTAPRESSURE

        [CommandMethod("WENTAPRESSURE")]
        public void Pressure()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers: ");
            if (sel == null) return;
            Fluid air = CadIo.PromptFluid(ed);
            if (air == null) return;
            try
            {
                DrawingTrace t = CadIo.Trace(ed, sel);
                t.Network.Solve(air);
                PressureReport report = PressureReport.Build(t.Network);
                AnalysisSummary summary = Analysis.Analyze(t.Network, air);

                CadIo.Lines(ed, report.ToText());
                ed.WriteMessage("\nBranches (flow, velocity, ΔP):");
                foreach (BranchInfo b in summary.Branches)
                    ed.WriteMessage(string.Format("\n  {0,-8} {1,-10} {2} m³/s  {3} m/s  {4} Pa", b.ComponentId, b.Kind,
                        F(b.FlowM3s, "0.000"), F(b.VelocityMs, "0.00"), F(b.PressureDropPa, "0.00")));

                string csv = Path.Combine(Path.GetTempPath(), "wenta_pressure.csv");
                File.WriteAllText(csv, report.ToCsv());

                List<string> critical = t.CriticalChains(report);
                Database db = doc.Database;
                double? previous;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CadIo.EnsureLayer(tr, db, CadIo.LayerCrit, 1);
                    CadIo.ClearLayer(tr, db, CadIo.LayerCrit);
                    BlockTableRecord ms = CadIo.ModelSpace(tr, db);
                    foreach (string id in critical)
                    {
                        Chain ch = t.ChainById(id);
                        if (ch == null) continue;
                        var pl = new ZwSoft.ZwCAD.DatabaseServices.Polyline(ch.Points.Count) { Layer = CadIo.LayerCrit };
                        for (int k = 0; k < ch.Points.Count; k++)
                            pl.AddVertexAt(k, new Point2d(ch.Points[k].X * 1000.0, ch.Points[k].Y * 1000.0), 0, 0, 0);
                        pl.ConstantWidth = 40.0;
                        ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                    }
                    previous = SwapLastDp(tr, db, report.CriticalDpPa);
                    tr.Commit();
                }

                string delta = previous.HasValue
                    ? string.Format("previous run {0} Pa, change {1}{2} Pa", F(previous.Value, "0.00"),
                        report.CriticalDpPa >= previous.Value ? "+" : "", F(report.CriticalDpPa - previous.Value, "0.00"))
                    : "first run in this drawing";
                ed.WriteMessage(string.Format(
                    "\nCritical-path ΔP {0} Pa (air ρ={1} kg/m³); {2}. Critical runs on {3}: {4}. CSV: {5}",
                    F(report.CriticalDpPa, "0.00"), F(air.Density, "0.000"), delta, CadIo.LayerCrit,
                    string.Join(" ", critical.ToArray()), csv));
                Commands.Log(string.Format(C, "WENTAPRESSURE ok  {0:R} Pa  rho {1:0.0000}  critical {2}  delta {3}",
                    report.CriticalDpPa, air.Density, string.Join(",", critical.ToArray()),
                    previous.HasValue ? (report.CriticalDpPa - previous.Value).ToString("0.0000", C) : "none"));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAPRESSURE: " + ex.Message);
                Commands.Log("WENTAPRESSURE FAILED " + ex.Message);
            }
        }

        // Stores the critical ΔP of this run in the named objects dictionary
        // and returns the one stored by the previous run.
        private static double? SwapLastDp(Transaction tr, Database db, double dp)
        {
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            double? previous = null;
            Xrecord xr;
            if (nod.Contains(LastDpKey))
            {
                xr = (Xrecord)tr.GetObject(nod.GetAt(LastDpKey), OpenMode.ForWrite);
                if (xr.Data != null)
                    foreach (TypedValue tv in xr.Data.AsArray())
                        if (tv.TypeCode == (int)DxfCode.Real) previous = Convert.ToDouble(tv.Value, C);
            }
            else
            {
                xr = new Xrecord();
                nod.SetAt(LastDpKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Real, dp));
            return previous;
        }

        // --------------------------------------------------------------- WENTASIZE

        [CommandMethod("WENTASIZE")]
        public void Size()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers to size: ");
            if (sel == null) return;

            string method = CadIo.PromptKeyword(ed,
                "\nMethod [Velocity/EqualFriction/Budget/Noise/Aspect] <Velocity>: ", "Velocity",
                "Velocity", "EqualFriction", "Budget", "Noise", "Aspect");
            if (method == null) return;
            string shape = CadIo.PromptKeyword(ed, "\nShape [Round/Rectangular] <Round>: ", "Round", "Round", "Rectangular");
            if (shape == null) return;

            var req = new BatchSizingRequest { Shape = shape == "Round" ? Sizing.ShapeRound : Sizing.ShapeRectangular };
            double? v;
            switch (method)
            {
                case "EqualFriction":
                    req.Method = SizingMethod.EqualFriction;
                    v = CadIo.PromptDouble(ed, "\nTarget friction [Pa/m] <1.0>: ", 1.0);
                    if (v == null) return;
                    req.TargetPaPerM = v.Value;
                    break;
                case "Budget":
                    req.Method = SizingMethod.PressureDropBudget;
                    v = CadIo.PromptDouble(ed, "\nPressure budget per run [Pa] <10>: ", 10.0);
                    if (v == null) return;
                    req.BudgetPa = v.Value;
                    break;
                case "Noise":
                    req.Method = SizingMethod.NoiseLimit;
                    string space = CadIo.PromptKeyword(ed,
                        "\nSpace type [studio/bedroom/office/classroom/retail/industrial] <office>: ", "office",
                        "studio", "bedroom", "office", "classroom", "retail", "industrial");
                    if (space == null) return;
                    req.SpaceType = space;
                    break;
                case "Aspect":
                    req.Method = SizingMethod.AspectRatio;
                    req.Shape = Sizing.ShapeRectangular;
                    v = CadIo.PromptDouble(ed, "\nTarget velocity [m/s] <4.0>: ", 4.0);
                    if (v == null) return;
                    req.TargetVelocity = v.Value;
                    v = CadIo.PromptDouble(ed, "\nAspect ratio w/h <2.0>: ", 2.0);
                    if (v == null) return;
                    req.AspectRatio = v.Value;
                    break;
                default:
                    req.Method = SizingMethod.Velocity;
                    v = CadIo.PromptDouble(ed, "\nTarget velocity [m/s] <4.0>: ", 4.0);
                    if (v == null) return;
                    req.TargetVelocity = v.Value;
                    break;
            }

            try
            {
                DrawingTrace t = CadIo.Trace(ed, sel);
                List<ChainSizing> plan = DrawingSizing.Plan(t, req);
                var byHandle = new Dictionary<string, DrawnDuct>();
                foreach (DrawnDuct d in sel.Ducts) byHandle[d.Handle] = d;

                Database db = doc.Database;
                int labelsNew = 0, labelsUpdated = 0, errors = 0;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CadIo.EnsureLayer(tr, db, CadIo.LayerLabel, 2);
                    foreach (ChainSizing cs in plan)
                    {
                        if (cs.Snapped == null)
                        {
                            errors++;
                            ed.WriteMessage(string.Format("\n  {0}: {1}", cs.ChainId, cs.Batch.Error));
                            continue;
                        }
                        foreach (string h in t.ChainHandles[cs.ChainId])
                        {
                            WentaTag tag = CadIo.TagFor(sel, h, WentaTag.ClassDuct);
                            tag.SetSection(cs.Snapped);
                            tag.Set("flow_m3s", cs.FlowM3s);
                            tag.Set("velocity_ms", cs.VelocityMs);
                            tag.Set("sizing_method", req.Method.ToString());
                            string before = tag.Get("label_h");
                            CadIo.UpsertText(tr, db, tag, "label_h", CadIo.LayerLabel,
                                CadIo.LabelPoint(byHandle[h], 60.0), TextHeightMm, cs.Label);
                            if (before != null && before == tag.Get("label_h")) labelsUpdated++; else labelsNew++;
                            Entity e = (Entity)tr.GetObject(sel.Ids[h], OpenMode.ForWrite);
                            CadIo.WriteTag(tr, db, e, tag);
                            byHandle[h].Section = cs.Snapped;
                        }
                        ed.WriteMessage("\n  " + cs.ChainId + "  " + cs.Label);
                    }
                    tr.Commit();
                }

                DrawingTrace resized = DrawingTrace.Build(sel.Ducts, sel.Markers,
                    new DrawingTraceOptions { Name = Path.GetFileNameWithoutExtension(doc.Name) });
                double dp = resized.Network.Solve();
                ed.WriteMessage(string.Format(
                    "\nSized {0} runs ({1} method, {2}); labels {3} new, {4} updated; {5} failed. Critical-path ΔP now {6} Pa.",
                    plan.Count - errors, req.Method, req.Shape, labelsNew, labelsUpdated, errors, F(dp, "0.00")));
                var sizes = new List<string>();
                foreach (ChainSizing cs in plan)
                    sizes.Add(cs.ChainId + "=" + (cs.Snapped != null ? DrawingSizing.SizeText(cs.Snapped) : "error"));
                Commands.Log(string.Format(C, "WENTASIZE ok  {0} {1}  {2}  labels new {3} updated {4}  {5:R} Pa",
                    req.Method, req.Shape, string.Join(" ", sizes.ToArray()), labelsNew, labelsUpdated, dp));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTASIZE: " + ex.Message);
                Commands.Log("WENTASIZE FAILED " + ex.Message);
            }
        }

        // ------------------------------------------------------------ WENTABALANCE

        [CommandMethod("WENTABALANCE")]
        public void Balance()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            CadIo.Selection sel = CadIo.Select(ed, "\nSelect duct centrelines and WENTATAG markers: ");
            if (sel == null) return;
            Fluid air = CadIo.PromptFluid(ed);
            if (air == null) return;
            try
            {
                DrawingTrace t = CadIo.Trace(ed, sel);
                double dp = t.Network.Solve(air);
                List<BalancingHint> hints = ReFit.Hints(t.Network, air);
                List<DamperSetting> dampers = DrawingBalancing.Dampers(t, hints);
                var markerById = new Dictionary<string, DrawnMarker>();
                foreach (DrawnMarker m in sel.Markers) markerById[m.Handle] = m;

                Database db = doc.Database;
                int hardware = 0;
                var csv = new StringBuilder();
                csv.Append("marker,class,terminal_id,run_id,available_dp_pa,required_dp_pa,surplus_dp_pa,damper_zeta,damper_open_percent,needs_hardware\r\n");
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ed.WriteMessage(string.Format("\nCritical-path ΔP {0} Pa. Terminal balancing:", F(dp, "0.00")));
                    foreach (BalancingHint h in hints)
                    {
                        bool hw = DrawingBalancing.NeedsHardware(h);
                        if (hw) hardware++;
                        string mh;
                        t.TerminalMarker.TryGetValue(h.TerminalId, out mh);
                        ed.WriteMessage(string.Format("\n  {0}  surplus {1} Pa  ζ={2}  {3}% open{4}", h.TerminalId,
                            F(h.SurplusDpPa, "0.00"), F(h.DamperZeta, "0.00"), F(h.DamperOpenPercent, "0"),
                            hw ? "  -> needs a hardware change" : ""));
                        AppendRow(csv, mh, WentaTag.ClassTerminal, h.TerminalId, t.ChainFeeding(h.TerminalId), h, hw);
                        if (mh != null && markerById.ContainsKey(mh)) WriteBalance(tr, db, sel, markerById[mh], h, hw);
                    }
                    foreach (DamperSetting s in dampers)
                    {
                        if (s.Hint == null)
                        {
                            ed.WriteMessage("\n  damper on " + s.ChainId + ": run does not feed a terminal directly; nothing written");
                            continue;
                        }
                        ed.WriteMessage(string.Format("\n  damper on {0} (-> {1})  ζ={2}  {3}% open{4}", s.ChainId, s.TerminalId,
                            F(s.Hint.DamperZeta, "0.00"), F(s.Hint.DamperOpenPercent, "0"),
                            s.NeedsHardware ? "  -> needs a hardware change" : ""));
                        AppendRow(csv, s.MarkerHandle, WentaTag.ClassDamper, s.TerminalId, s.ChainId, s.Hint, s.NeedsHardware);
                        WriteBalance(tr, db, sel, markerById[s.MarkerHandle], s.Hint, s.NeedsHardware);
                    }
                    tr.Commit();
                }

                string path = Path.Combine(Path.GetTempPath(), "wenta_balance.csv");
                File.WriteAllText(path, csv.ToString());
                ed.WriteMessage(string.Format("\n{0} terminals, {1} dampers written; {2} branch(es) need a hardware change. Schedule: {3}",
                    hints.Count, dampers.Count, hardware, path));
                var parts = new List<string>();
                foreach (BalancingHint h in hints)
                    parts.Add(h.TerminalId + " zeta " + h.DamperZeta.ToString("0.0000", C) + " open " + h.DamperOpenPercent.ToString("0.00", C));
                Commands.Log(string.Format(C, "WENTABALANCE ok  {0:R} Pa  {1}  dampers {2}  hardware {3}",
                    dp, string.Join("; ", parts.ToArray()), dampers.Count, hardware));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTABALANCE: " + ex.Message);
                Commands.Log("WENTABALANCE FAILED " + ex.Message);
            }
        }

        private static void AppendRow(StringBuilder csv, string marker, string cls, string terminal, string run,
            BalancingHint h, bool hardware)
        {
            csv.Append(string.Join(",", new[]
            {
                marker ?? "", cls, terminal ?? "", run ?? "",
                h.AvailableDpPa.ToString("R", C), h.RequiredDpPa.ToString("R", C), h.SurplusDpPa.ToString("R", C),
                h.DamperZeta.ToString("R", C), h.DamperOpenPercent.ToString("R", C), hardware ? "1" : "0",
            })).Append("\r\n");
        }

        private static void WriteBalance(Transaction tr, Database db, CadIo.Selection sel, DrawnMarker m,
            BalancingHint h, bool hardware)
        {
            WentaTag tag = CadIo.TagFor(sel, m.Handle, m.Class);
            tag.Set("balance_zeta", h.DamperZeta);
            tag.Set("balance_open_pct", h.DamperOpenPercent);
            tag.Set("balance_surplus_pa", h.SurplusDpPa);
            tag.Set("needs_hardware", hardware ? "1" : "0");
            Circle c = (Circle)tr.GetObject(sel.Ids[m.Handle], OpenMode.ForWrite);
            CadIo.UpsertText(tr, db, tag, "label_h", CadIo.LayerTag,
                new Point3d(c.Center.X + MarkerRadiusMm * 1.2, c.Center.Y + MarkerRadiusMm * 1.2, 0.0),
                TextHeightMm, MarkerText(tag));
            CadIo.WriteTag(tr, db, c, tag);
        }
    }
}
