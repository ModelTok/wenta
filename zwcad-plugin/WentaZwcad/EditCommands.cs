using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using Wenta;

[assembly: CommandClass(typeof(WentaZwcad.EditCommands))]

namespace WentaZwcad
{
    // Editing the drawn network (#23) and re-fitting it after a flow change
    // (#27). Each command reads the component records (as WENTAREAD does),
    // makes the edit in Wenta.NetworkEdit, and redraws the records in place:
    // the same entities, GUIDs and XData, with the lengths, sizes and labels
    // of the edited network, so a later WENTAREAD finds no length edits.
    public class EditCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // The drawn network, or null (with the reason shown and logged).
        private static NetworkReadResult ReadDrawn(Document doc, string cmd)
        {
            var ids = new Dictionary<string, ObjectId>();
            NetworkReadResult r = NetworkDrawing.Read(RoundTripCommands.Scan(doc.Database, ids),
                Path.GetFileNameWithoutExtension(doc.Name));
            if (r.Ok) return r;
            string first = r.Errors.Count > 0 ? r.Errors[0] : "no component records";
            doc.Editor.WriteMessage(string.Format(C,
                "\n{0}: the drawn network has {1} problem(s) (first: {2}); run WENTAREAD for the list.",
                cmd, r.Errors.Count, first));
            Commands.Log(string.Format(C, "{0} REFUSED {1} problem(s): {2}", cmd, r.Errors.Count, first));
            return null;
        }

        // A component id typed at the prompt, or picked (Enter, then pick).
        private static string PromptComponent(Editor ed, NetworkReadResult r, string message)
        {
            var o = new PromptStringOptions(message + " (Enter to pick): ") { AllowSpaces = false };
            PromptResult s = ed.GetString(o);
            if (s.Status != PromptStatus.OK) return null;
            string cid = (s.StringResult ?? "").Trim();
            if (cid.Length == 0)
            {
                PromptEntityResult pe = ed.GetEntity("\nPick a WENTA component: ");
                if (pe.Status != PromptStatus.OK) return null;
                using (Transaction tr = ed.Document.Database.TransactionManager.StartTransaction())
                {
                    Entity e = (Entity)tr.GetObject(pe.ObjectId, OpenMode.ForRead);
                    WentaTag tag = CadIo.ReadTag(e);
                    cid = tag != null && NetworkDrawing.IsRecord(tag) ? tag.Get("cid") : null;
                    tr.Commit();
                }
                if (cid == null) { ed.WriteMessage("\nThat entity is not a WENTA component record."); return null; }
            }
            if (!r.Network.Components.ContainsKey(cid))
            {
                ed.WriteMessage("\nNo component '" + cid + "' in the drawn network.");
                return null;
            }
            return cid;
        }

        private static int Redraw(Document doc, Network net, Dictionary<string, ComponentMeta> meta)
        {
            var warnings = new List<string>();
            List<DrawnComponent> items = NetworkDrawing.Write(net, meta, new Wenta.Point2(0.0, 0.0), warnings);
            foreach (string w in warnings) doc.Editor.WriteMessage("\nWarning: " + w);
            int created, updated, stale;
            RoundTripCommands.Draw(doc, items, out created, out updated, out stale);
            return updated;
        }

        private static string Value(string property, double? v)
        {
            if (v == null) return "-";
            switch (property)
            {
                case "diameter": case "width": case "height": return v.Value.ToString("0", C) + " mm";
                case "length": return v.Value.ToString("0.###", C) + " m";
                case "flow": return v.Value.ToString("0.#####", C) + " m³/s";
                default: return v.Value.ToString("0.###", C);
            }
        }

        private static void Report(Editor ed, string cmd, EditResult res, int updated)
        {
            if (res.Note != null) ed.WriteMessage("\n" + res.Note + ".");
            ed.WriteMessage(string.Format(C,
                "\n{0}: {1} record(s) changed ({2}), {3} redrawn in place; critical-path ΔP {4} -> {5} Pa.",
                cmd, res.Changed.Count, string.Join(", ", res.Changed.ToArray()), updated,
                res.CriticalBeforePa.ToString("0.00", C), res.CriticalAfterPa.ToString("0.00", C)));
        }

        // ------------------------------------------------------------ WENTAPROPS

        [CommandMethod("WENTAPROPS")]
        public void PropsCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            NetworkReadResult r = ReadDrawn(doc, "WENTAPROPS");
            if (r == null) return;
            string cid = PromptComponent(ed, r, "\nComponent id");
            if (cid == null) return;
            Component c = r.Network.Components[cid];
            r.Network.Solve();

            var have = new List<string>();
            ed.WriteMessage(string.Format(C, "\n{0} ({1})", cid, NetworkJson.WentaClassOf(c)));
            foreach (string p in NetworkEdit.Properties)
            {
                double? v = NetworkEdit.Get(c, p);
                if (v == null) continue;
                have.Add(p);
                ed.WriteMessage(string.Format(C, "\n  {0,-9} {1}", p, Value(p, v)));
            }
            double? q = c.InletFlowrate();
            if (q != null) ed.WriteMessage(string.Format(C, "\n  carries   {0} m³/s", q.Value.ToString("0.#####", C)));
            if (have.Count == 0) return;

            var keys = new List<string>(have) { "Done" };
            string prop = CadIo.PromptKeyword(ed, "\nProperty to edit [" + string.Join("/", keys.ToArray()) + "] <Done>: ",
                "Done", keys.ToArray());
            if (prop == null || prop == "Done") return;
            string unit = prop == "length" ? "m" : prop == "flow" ? "m³/s" : prop == "zeta" ? "-" : "mm";
            double? val = CadIo.PromptDouble(ed, string.Format(C, "\nNew {0} [{1}] <{2}>: ", prop, unit,
                NetworkEdit.Get(c, prop).Value.ToString("0.#####", C)), NetworkEdit.Get(c, prop).Value, prop == "zeta");
            if (val == null) return;
            try
            {
                EditResult res = NetworkEdit.Set(r.Network, r.Meta, cid, prop, val.Value);
                int updated = Redraw(doc, res.Network, res.Meta);
                Report(ed, "WENTAPROPS", res, updated);
                Commands.Log(string.Format(C, "WENTAPROPS ok  {0}.{1} = {2}  {3} -> {4}  changed {5}  dp {6:R} -> {7:R} Pa",
                    cid, prop, val.Value.ToString("R", C), Value(prop, NetworkEdit.Get(c, prop)),
                    Value(prop, NetworkEdit.Get(res.Network.Components[cid], prop)), res.Changed.Count,
                    res.CriticalBeforePa, res.CriticalAfterPa));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAPROPS: " + ex.Message);
                Commands.Log("WENTAPROPS FAILED " + ex.Message);
            }
        }

        // ------------------------------------------------------------- WENTAMOVE

        [CommandMethod("WENTAMOVE")]
        public void MoveCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            NetworkReadResult r = ReadDrawn(doc, "WENTAMOVE");
            if (r == null) return;
            string cid = PromptComponent(ed, r, "\nComponent to move (tee, fitting, damper, terminal, source)");
            if (cid == null) return;
            double? dx = CadIo.PromptDouble(ed, "\nDisplacement X [mm] <0>: ", 0.0, true, true);
            if (dx == null) return;
            double? dy = CadIo.PromptDouble(ed, "\nDisplacement Y [mm] <0>: ", 0.0, true, true);
            if (dy == null) return;
            try
            {
                EditResult res = NetworkEdit.Move(r.Network, r.Meta, cid, dx.Value, dy.Value);
                int updated = Redraw(doc, res.Network, res.Meta);
                Report(ed, "WENTAMOVE", res, updated);
                Commands.Log(string.Format(C, "WENTAMOVE ok  {0} by {1:R},{2:R} mm  changed {3}  dp {4:R} -> {5:R} Pa",
                    cid, dx.Value, dy.Value, res.Changed.Count, res.CriticalBeforePa, res.CriticalAfterPa));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAMOVE: " + ex.Message);
                Commands.Log("WENTAMOVE FAILED " + ex.Message);
            }
        }

        // ---------------------------------------------------------- WENTASTRETCH

        [CommandMethod("WENTASTRETCH")]
        public void StretchCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            NetworkReadResult r = ReadDrawn(doc, "WENTASTRETCH");
            if (r == null) return;
            string cid = PromptComponent(ed, r, "\nDuct to stretch");
            if (cid == null) return;
            double? cur = NetworkEdit.Get(r.Network.Components[cid], "length");
            if (cur == null) { ed.WriteMessage("\n'" + cid + "' is not a duct."); return; }
            double? len = CadIo.PromptDouble(ed, string.Format(C, "\nNew length [m] <{0}>: ", cur.Value.ToString("0.###", C)), cur.Value);
            if (len == null) return;
            try
            {
                EditResult res = NetworkEdit.Stretch(r.Network, r.Meta, cid, len.Value);
                int updated = Redraw(doc, res.Network, res.Meta);
                Report(ed, "WENTASTRETCH", res, updated);
                Commands.Log(string.Format(C, "WENTASTRETCH ok  {0} {1:R} -> {2:R} m  changed {3}  dp {4:R} -> {5:R} Pa",
                    cid, cur.Value, len.Value, res.Changed.Count, res.CriticalBeforePa, res.CriticalAfterPa));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTASTRETCH: " + ex.Message);
                Commands.Log("WENTASTRETCH FAILED " + ex.Message);
            }
        }

        // ------------------------------------------------------------ WENTAREFIT

        // #27: one terminal's new flow; every duct re-sized for its flow, the
        // network re-solved, and each damper (role "damper") set so its
        // terminal's path matches the critical one. All in one command.
        [CommandMethod("WENTAREFIT")]
        public void ReFitCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            NetworkReadResult r = ReadDrawn(doc, "WENTAREFIT");
            if (r == null) return;
            string cid = PromptComponent(ed, r, "\nTerminal");
            if (cid == null) return;
            var t = r.Network.Components[cid] as Terminal;
            if (t == null) { ed.WriteMessage("\n'" + cid + "' is not a terminal."); return; }
            double? q = CadIo.PromptDouble(ed, string.Format(C, "\nNew flow [m³/s] <{0}>: ", t.Flowrate.ToString("0.#####", C)), t.Flowrate);
            if (q == null) return;
            string method = CadIo.PromptKeyword(ed, "\nRe-size by [Velocity/EqualFriction] <Velocity>: ", "Velocity",
                "Velocity", "EqualFriction");
            if (method == null) return;
            var opt = new ReFitOptions();
            if (method == "EqualFriction")
            {
                opt.Method = SizingMethod.EqualFriction;
                double? f = CadIo.PromptDouble(ed, "\nTarget friction [Pa/m] <1.0>: ", 1.0);
                if (f == null) return;
                opt.TargetPaPerM = f.Value;
            }
            else
            {
                double? v = CadIo.PromptDouble(ed, "\nTarget velocity [m/s] <4.0>: ", 4.0);
                if (v == null) return;
                opt.TargetVelocity = v.Value;
            }
            try
            {
                ReFlowResult res = NetworkEdit.ReFlow(r.Network, r.Meta, cid, q.Value, opt);
                foreach (ReFitChange ch in res.Resized)
                    ed.WriteMessage(string.Format(C, "\n  {0,-10} Ø{1} -> Ø{2}  {3} -> {4} m/s", ch.ComponentId,
                        (ch.OldDiameterM * 1000.0).ToString("0", C), (ch.NewDiameterM * 1000.0).ToString("0", C),
                        ch.OldVelocityMs.ToString("0.00", C), ch.NewVelocityMs.ToString("0.00", C)));
                foreach (DamperSettingResult d in res.Dampers)
                    ed.WriteMessage(string.Format(C, "\n  damper {0,-8} for {1,-8} ζ {2}  open {3} %", d.DamperId, d.TerminalId,
                        d.Zeta.ToString("0.00", C), d.OpenPercent.ToString("0", C)));
                foreach (BalancingHint h in res.Hints)
                    if (r.Network.Components.ContainsKey(h.TerminalId) && NetworkEdit.DamperFeeding(res.Network, res.Meta, h.TerminalId) == null
                        && h.SurplusDpPa > 0.01)
                        ed.WriteMessage(string.Format(C, "\n  {0,-8} has {1} Pa surplus and no damper (ζ {2}, {3} % open would balance it)",
                            h.TerminalId, h.SurplusDpPa.ToString("0.00", C), h.DamperZeta.ToString("0.00", C),
                            h.DamperOpenPercent.ToString("0", C)));
                int updated = Redraw(doc, res.Network, res.Meta);
                string json = Path.Combine(Path.GetTempPath(), "wenta_refit.json");
                File.WriteAllText(json, NetworkJson.SerializeDesign(res.Network, res.Meta));
                ed.WriteMessage(string.Format(C,
                    "\nWENTAREFIT: {0} {1} -> {2} m³/s, {3} duct(s) re-sized, {4} damper(s) set, {5} record(s) redrawn;" +
                    " critical-path ΔP {6} -> {7} Pa, residual imbalance {8} Pa, {9} ms.\nNetwork JSON: {10}",
                    cid, res.OldFlowM3s.ToString("0.#####", C), res.NewFlowM3s.ToString("0.#####", C), res.Resized.Count,
                    res.Dampers.Count, updated, res.CriticalBeforePa.ToString("0.00", C), res.CriticalAfterPa.ToString("0.00", C),
                    res.ResidualSurplusPa.ToString("0.000", C), res.ElapsedMs.ToString("0.0", C), json));
                Commands.Log(string.Format(C,
                    "WENTAREFIT ok  {0} {1:R} -> {2:R}  resized {3}  dampers {4}  dp {5:R} -> {6:R} Pa  residual {7:R}  {8:0.0} ms",
                    cid, res.OldFlowM3s, res.NewFlowM3s, res.Resized.Count, res.Dampers.Count,
                    res.CriticalBeforePa, res.CriticalAfterPa, res.ResidualSurplusPa, res.ElapsedMs));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAREFIT: " + ex.Message);
                Commands.Log("WENTAREFIT FAILED " + ex.Message);
            }
        }
    }
}
