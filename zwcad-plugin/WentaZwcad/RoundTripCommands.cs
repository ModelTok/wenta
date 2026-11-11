using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    // component records back and saves the JSON. The mapping, layout and
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

                int created = 0, updated = 0, stale = 0;
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

            var items = new List<DrawnComponent>();
            var ids = new Dictionary<string, ObjectId>();
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

        // ------------------------------------------------------------- helpers

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
