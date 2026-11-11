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

[assembly: CommandClass(typeof(WentaZwcad.RoundTripCommands))]

namespace WentaZwcad
{
    // Network JSON <-> drawing (#20, #22). WENTAWRITE draws a network JSON
    // to scale on WENTA-DUCT with labels on WENTA-ANNO; WENTAREAD reads the
    // component records back and saves the JSON; WENTAATTEN (#48) adds
    // attenuators to the drawn network. The mapping, layout and
    // validation are Wenta.NetworkDrawing; this class only talks to ZWCAD.
    public class RoundTripCommands
    {
        private const double MarkerRadiusMm = 100.0;
        private const double TextHeightMm = 80.0;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static string DefaultJson()
        {
            return Path.Combine(Path.GetTempPath(), "wenta_read.json");
        }

        private static string PromptPath(Editor ed, string message, string def)
        {
            var o = new PromptStringOptions(message + " <" + def + ">: ") { AllowSpaces = true };
            PromptResult r = ed.GetString(o);
            if (r.Status != PromptStatus.OK) return null;
            string s = (r.StringResult ?? "").Trim().Trim('"');
            return s.Length == 0 ? def : Environment.ExpandEnvironmentVariables(s);
        }

        // ------------------------------------------------------------- WENTAWRITE

        [CommandMethod("WENTAWRITE")]
        public void WriteCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            string path = PromptPath(ed, "\nNetwork JSON to draw", DefaultJson());
            if (path == null) return;
            // A bare file name not found here may be one of the examples
            // shipped next to the plugin (example-dampers-network.json).
            if (!File.Exists(path) && !Path.IsPathRooted(path) && File.Exists(Path.Combine(PluginDir(), path)))
                path = Path.Combine(PluginDir(), path);

            var po = new PromptPointOptions("\nBase point for a new layout <0,0>: ") { AllowNone = true };
            PromptPointResult pr = ed.GetPoint(po);
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
            var origin = pr.Status == PromptStatus.OK ? new Wenta.Point2(pr.Value.X, pr.Value.Y) : new Wenta.Point2(0.0, 0.0);

            try
            {
                Dictionary<string, ComponentMeta> meta;
                Network net = NetworkJson.Load(path, out meta);
                var warnings = new List<string>();
                List<DrawnComponent> items = NetworkDrawing.Write(net, meta, origin, warnings);
                foreach (string w in warnings) ed.WriteMessage("\nWarning: " + w);

                int created, updated, stale;
                Draw(doc, items, out created, out updated, out stale);

                ed.WriteMessage(string.Format(C,
                    "\nWENTAWRITE: {0} components from {1} ({2} new, {3} updated in place) on {4}, labels on {5}.",
                    items.Count, path, created, updated, NetworkDrawing.LayerDuct, NetworkDrawing.LayerAnno));
                if (stale > 0)
                    ed.WriteMessage(string.Format(C,
                        "\nWarning: {0} component record(s) in the drawing are not in this JSON; WENTAREAD will refuse them until they are erased or connected.",
                        stale));
                Commands.Log(string.Format(C, "WENTAWRITE ok  {0} components  new {1}  updated {2}  stale {3}  {4}",
                    items.Count, created, updated, stale, Path.GetFileName(path)));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAWRITE: " + ex.Message);
                Commands.Log("WENTAWRITE FAILED " + ex.Message);
            }
        }

        // -------------------------------------------------------------- WENTAREAD

        [CommandMethod("WENTAREAD")]
        public void ReadCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var ids = new Dictionary<string, ObjectId>();
            List<DrawnComponent> items = Scan(db, ids);

            string name = Path.GetFileNameWithoutExtension(doc.Name);
            NetworkReadResult r = NetworkDrawing.Read(items, name);
            if (!r.Ok)
            {
                int shown = 0;
                foreach (string err in r.Errors)
                {
                    if (shown++ == 20) { ed.WriteMessage("\n  ..."); break; }
                    ed.WriteMessage("\n  " + err);
                }
                ed.WriteMessage(string.Format(C, "\nWENTAREAD refused: {0} problem(s) with the WENTA tags; no JSON written.",
                    r.Errors.Count));
                Commands.Log(string.Format(C, "WENTAREAD REFUSED {0} problem(s): {1}", r.Errors.Count, r.Errors[0]));
                return;
            }

            string path = PromptPath(ed, "\nSave network JSON as", DefaultJson());
            if (path == null) return;
            try
            {
                // Edited centrelines: the record takes the drawn length.
                if (r.LengthUpdated.Count > 0)
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (string cid in r.LengthUpdated)
                        {
                            ObjectId id;
                            if (!ids.TryGetValue(r.HandleOf[cid], out id)) continue;
                            Entity e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            WentaTag tag = CadIo.ReadTag(e);
                            Component c = r.Network.Components[cid];
                            NetworkDrawing.MergeInto(tag, NetworkDrawing.Record(r.Network, cid, c, r.Meta[cid]));
                            var item = new DrawnComponent { PointsMm = r.Meta[cid].LayoutMm };
                            CadIo.UpsertText(tr, db, tag, "label_h", NetworkDrawing.LayerAnno, LabelAt(item),
                                TextHeightMm, NetworkDrawing.LabelText(cid, c, r.Meta[cid]));
                            CadIo.WriteTag(tr, db, e, tag);
                        }
                        tr.Commit();
                    }

                File.WriteAllText(path, NetworkJson.SerializeDesign(r.Network, r.Meta));
                double dp = r.Network.Solve();
                ed.WriteMessage(string.Format(C,
                    "\nWENTAREAD: {0} components, {1} length(s) taken from edited geometry, {2} other WENTA tag(s) left alone." +
                    "\nCritical-path ΔP {3} Pa. Network JSON: {4}",
                    r.Network.Components.Count, r.LengthUpdated.Count, r.Ignored, dp.ToString("0.00", C), path));
                Commands.Log(string.Format(C, "WENTAREAD ok  {0} components  edits {1}  other {2}  {3:R} Pa  {4}",
                    r.Network.Components.Count, r.LengthUpdated.Count, r.Ignored, dp, Path.GetFileName(path)));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAREAD: " + ex.Message);
                Commands.Log("WENTAREAD FAILED " + ex.Message);
            }
        }

        // ------------------------------------------------------------- WENTAATTEN

        private static string PluginDir()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        // #48: room sound of the drawn network (the one WENTAWRITE draws and
        // WENTAREAD reads), an attenuator from the catalog for every room
        // over its NC limit, spliced in after the run feeding the terminal
        // and drawn in place; the solve includes its pressure loss.
        [CommandMethod("WENTAATTEN")]
        public void AttenCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var ids = new Dictionary<string, ObjectId>();
            NetworkReadResult r = NetworkDrawing.Read(Scan(db, ids), Path.GetFileNameWithoutExtension(doc.Name));
            if (!r.Ok)
            {
                ed.WriteMessage(string.Format(C,
                    "\nWENTAATTEN: the drawn network has {0} problem(s) (first: {1}); run WENTAREAD for the list.",
                    r.Errors.Count, r.Errors[0]));
                Commands.Log(string.Format(C, "WENTAATTEN REFUSED {0} problem(s): {1}", r.Errors.Count, r.Errors[0]));
                return;
            }
            string space = CadIo.PromptKeyword(ed,
                "\nSpace type [studio/bedroom/office/classroom/retail/industrial] <office>: ", "office",
                "studio", "bedroom", "office", "classroom", "retail", "industrial");
            if (space == null) return;
            double? area = CadIo.PromptDouble(ed, "\nRoom surface area [m²] <60>: ", 60.0);
            if (area == null) return;
            double? alpha = CadIo.PromptDouble(ed, "\nMean absorption coefficient α (0..1) <0.2>: ", 0.2);
            if (alpha == null) return;
            string path = PromptPath(ed, "\nAttenuator catalog JSON", Path.Combine(PluginDir(), "example-attenuators.json"));
            if (path == null) return;

            try
            {
                if (!File.Exists(path)) throw new WentaException("attenuator catalog not found: " + path);
                AttenuatorCatalog cat = AttenuatorCatalog.Load(path);
                double before = r.Network.Solve();
                AttenuatorResult res = AttenuatorSelection.Apply(r.Network, r.Meta, cat, space, area.Value, alpha.Value);

                var csv = new StringBuilder("terminal,run,velocity_ms,sound_power_db,pressure_level_db,nc_limit,required_db,attenuator,insertion_loss_db,level_after_db,ok,note\n");
                foreach (RoomAttenuation room in res.Rooms)
                {
                    string what = room.Picked != null ? "+ " + room.Picked.Id + " (" + room.InsertedId + ", "
                            + room.Picked.InsertionLossDb.ToString("0.0", C) + " dB, ζ " + room.Picked.Zeta.ToString("0.00", C) + ")"
                        : room.ExistingId != null ? "fitted " + room.ExistingId
                        : room.Problem ?? "no attenuator needed";
                    if (room.DuctId == null)
                        ed.WriteMessage(string.Format("\n  {0,-8} {1}", room.TerminalId, room.Problem));
                    else
                        ed.WriteMessage(string.Format(C, "\n  {0,-8} via {1,-8} Lp {2} dB  NC {3}  need {4} dB  {5}  -> {6} dB  {7}",
                            room.TerminalId, room.DuctId, room.PressureLevelDb.ToString("0.0", C), room.NcLimit.ToString("0", C),
                            room.RequiredDb.ToString("0.0", C), what, room.LevelAfterDb.ToString("0.0", C), room.Ok ? "ok" : "TOO LOUD"));
                    csv.Append(string.Join(",", new[]
                    {
                        room.TerminalId, room.DuctId ?? "", room.VelocityMs.ToString("R", C), room.SoundPowerDb.ToString("R", C),
                        room.PressureLevelDb.ToString("R", C), room.NcLimit.ToString("R", C), room.RequiredDb.ToString("R", C),
                        room.Picked != null ? room.Picked.Id : room.ExistingId ?? "",
                        room.Picked != null ? room.Picked.InsertionLossDb.ToString("R", C) : room.ExistingLossDb.ToString("R", C),
                        room.LevelAfterDb.ToString("R", C), room.Ok ? "1" : "0", (room.Problem ?? "").Replace(",", ";"),
                    })).Append('\n');
                }
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "wenta_atten.csv"), csv.ToString());

                int created = 0, updated = 0, stale = 0;
                if (res.Inserted > 0)
                {
                    var warnings = new List<string>();
                    List<DrawnComponent> items = NetworkDrawing.Write(res.Network, res.Meta, new Wenta.Point2(0.0, 0.0), warnings);
                    foreach (string w in warnings) ed.WriteMessage("\nWarning: " + w);
                    Draw(doc, items, out created, out updated, out stale);
                }
                string json = Path.Combine(Path.GetTempPath(), "wenta_atten.json");
                File.WriteAllText(json, NetworkJson.SerializeDesign(res.Network, res.Meta));
                int loud = res.Rooms.FindAll(x => !x.Ok).Count;
                ed.WriteMessage(string.Format(C,
                    "\nWENTAATTEN: {0} attenuator(s) inserted ({1} drawn), {2} of {3} rooms still over the limit; critical-path ΔP {4} -> {5} Pa." +
                    "\nNetwork JSON: {6}; table: {7}",
                    res.Inserted, created, loud, res.Rooms.Count, before.ToString("0.00", C), res.CriticalPa.ToString("0.00", C),
                    json, Path.Combine(Path.GetTempPath(), "wenta_atten.csv")));
                Commands.Log(string.Format(C, "WENTAATTEN ok  {0}  rooms {1}  inserted {2}  drawn {3}  over {4}  dp {5:R} -> {6:R} Pa",
                    space, res.Rooms.Count, res.Inserted, created, loud, before, res.CriticalPa));
            }
            catch (WentaException ex)
            {
                ed.WriteMessage("\nWENTAATTEN: " + ex.Message);
                Commands.Log("WENTAATTEN FAILED " + ex.Message);
            }
        }

        // ------------------------------------------------------------- helpers

        // Draws the items, updating earlier records in place (matched by
        // GUID, then by scope and id); counts records of the same scopes
        // that the items no longer contain.
        internal static void Draw(Document doc, List<DrawnComponent> items, out int created, out int updated, out int stale)
        {
            created = 0; updated = 0; stale = 0;
            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadIo.EnsureLayer(tr, db, NetworkDrawing.LayerDuct, 4);
                CadIo.EnsureLayer(tr, db, NetworkDrawing.LayerAnno, 2);
                BlockTableRecord ms = CadIo.ModelSpace(tr, db);

                // Existing records, by GUID and by scope + id (JSON from
                // elsewhere may carry no GUIDs).
                var byGuid = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                var byCid = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
                foreach (ObjectId id in ms)
                {
                    Entity e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    WentaTag t = CadIo.ReadTag(e);
                    if (!NetworkDrawing.IsRecord(t)) continue;
                    if (!string.IsNullOrEmpty(t.Guid) && !byGuid.ContainsKey(t.Guid)) byGuid[t.Guid] = id;
                    string key = (t.Get("scope") ?? "") + "|" + t.Get("cid");
                    if (!byCid.ContainsKey(key)) byCid[key] = id;
                }
                var touched = new HashSet<ObjectId>();

                foreach (DrawnComponent item in items)
                {
                    WentaTag record = item.Tag;
                    ObjectId found = ObjectId.Null;
                    if (!string.IsNullOrEmpty(record.Guid)) byGuid.TryGetValue(record.Guid, out found);
                    if (found.IsNull)
                        byCid.TryGetValue((record.Get("scope") ?? "") + "|" + record.Get("cid"), out found);

                    Entity ent = null;
                    WentaTag tag = null;
                    if (!found.IsNull && !touched.Contains(found))
                    {
                        ent = (Entity)tr.GetObject(found, OpenMode.ForWrite);
                        bool fits = item.PointsMm != null ? ent is ZwSoft.ZwCAD.DatabaseServices.Polyline : ent is Circle;
                        if (fits) tag = CadIo.ReadTag(ent);
                        else { ent.Erase(); ent = null; }
                    }

                    if (ent == null)
                    {
                        ent = item.PointsMm != null ? (Entity)NewPolyline(item.PointsMm)
                            : new Circle(P3(item.AtMm.Value), Vector3d.ZAxis, MarkerRadiusMm);
                        ent.Layer = NetworkDrawing.LayerDuct;
                        ms.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                        tag = new WentaTag();
                        created++;
                    }
                    else
                    {
                        if (item.PointsMm != null) SetVertices((ZwSoft.ZwCAD.DatabaseServices.Polyline)ent, item.PointsMm);
                        else ((Circle)ent).Center = P3(item.AtMm.Value);
                        tag = tag ?? new WentaTag();
                        updated++;
                    }
                    touched.Add(ent.ObjectId);

                    NetworkDrawing.MergeInto(tag, record);
                    CadIo.UpsertText(tr, db, tag, "label_h", NetworkDrawing.LayerAnno, LabelAt(item),
                        TextHeightMm, item.Label);
                    CadIo.WriteTag(tr, db, ent, tag);
                }

                var scopes = new HashSet<string>();
                foreach (DrawnComponent item in items) scopes.Add(item.Tag.Get("scope") ?? "");
                foreach (KeyValuePair<string, ObjectId> kv in byCid)
                    if (!touched.Contains(kv.Value) && !kv.Value.IsErased
                        && scopes.Contains(kv.Key.Substring(0, kv.Key.IndexOf('|'))))
                        stale++;
                tr.Commit();
            }

        }

        // Every WENTA-tagged entity of model space, with its geometry.
        internal static List<DrawnComponent> Scan(Database db, Dictionary<string, ObjectId> ids)
        {
            var items = new List<DrawnComponent>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in CadIo.ModelSpace(tr, db))
                {
                    Entity e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    WentaTag tag = CadIo.ReadTag(e);
                    if (tag == null) continue;
                    var d = new DrawnComponent { Handle = e.Handle.ToString(), Tag = tag };
                    Circle circle = e as Circle;
                    if (circle != null) d.AtMm = new Wenta.Point2(circle.Center.X, circle.Center.Y);
                    else d.PointsMm = CadIo.Vertices(e);
                    items.Add(d);
                    ids[d.Handle] = id;
                }
                tr.Commit();
            }

            return items;
        }


        private static Point3d P3(Wenta.Point2 p) { return new Point3d(p.X, p.Y, 0.0); }

        private static ZwSoft.ZwCAD.DatabaseServices.Polyline NewPolyline(List<Wenta.Point2> pts)
        {
            var pl = new ZwSoft.ZwCAD.DatabaseServices.Polyline(pts.Count);
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            return pl;
        }

        // A polyline cannot drop to zero vertices, so the new ones go in
        // first and the old ones are removed after.
        private static void SetVertices(ZwSoft.ZwCAD.DatabaseServices.Polyline pl, List<Wenta.Point2> pts)
        {
            int old = pl.NumberOfVertices;
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(old + i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            for (int i = 0; i < old; i++) pl.RemoveVertexAt(0);
        }

        private static Point3d LabelAt(DrawnComponent item)
        {
            if (item.PointsMm != null && item.PointsMm.Count >= 2)
            {
                var d = new DrawnDuct();
                d.PointsMm.AddRange(item.PointsMm);
                return CadIo.LabelPoint(d, TextHeightMm);
            }
            Wenta.Point2 p = item.AtMm ?? new Wenta.Point2(0.0, 0.0);
            return new Point3d(p.X + MarkerRadiusMm * 1.2, p.Y + MarkerRadiusMm * 1.2, 0.0);
        }
    }
}
