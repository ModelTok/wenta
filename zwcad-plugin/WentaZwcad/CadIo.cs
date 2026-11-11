using System;
using System.Collections.Generic;
using System.Globalization;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using Wenta;

namespace WentaZwcad
{
    // ZWCAD side of the drawing adapter: reads selected centrelines and
    // markers into Wenta.DrawnDuct / DrawnMarker, and writes WENTA tags,
    // labels and overlay layers back. Drawing units are millimetres.
    internal static class CadIo
    {
        internal const string LayerTag = "WENTA-TAG";
        internal const string LayerLabel = "WENTA-LABEL";
        internal const string LayerCrit = "WENTA-CRIT";
        internal const string LayerInsul = "WENTA-INSUL";

        internal static readonly CultureInfo C = CultureInfo.InvariantCulture;

        internal sealed class Selection
        {
            public readonly List<DrawnDuct> Ducts = new List<DrawnDuct>();
            public readonly List<DrawnMarker> Markers = new List<DrawnMarker>();
            public readonly Dictionary<string, ObjectId> Ids = new Dictionary<string, ObjectId>();
            public readonly Dictionary<string, WentaTag> Tags = new Dictionary<string, WentaTag>();
            public int Skipped;
        }

        internal static Selection Select(Editor ed, string prompt)
        {
            var opts = new PromptSelectionOptions { MessageForAdding = prompt };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,ARC,CIRCLE") });
            PromptSelectionResult res = ed.GetSelection(opts, filter);
            if (res.Status != PromptStatus.OK) return null;
            var sel = new Selection();
            Database db = ed.Document.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in res.Value.GetObjectIds())
                {
                    Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (e.Layer == LayerLabel || e.Layer == LayerCrit || e.Layer == LayerInsul) continue;
                    string h = e.Handle.ToString();
                    WentaTag tag = ReadTag(e);
                    var circle = e as Circle;
                    if (circle != null)
                    {
                        if (tag == null || tag.Legacy || (tag.Class != WentaTag.ClassTerminal
                            && tag.Class != WentaTag.ClassSource && tag.Class != WentaTag.ClassDamper))
                        {
                            sel.Skipped++;
                            continue;
                        }
                        sel.Markers.Add(new DrawnMarker
                        {
                            Handle = h,
                            Class = tag.Class,
                            AtMm = new Wenta.Point2(circle.Center.X, circle.Center.Y),
                            FlowM3s = tag.GetDouble("flow_m3s") ?? 0.0,
                        });
                    }
                    else
                    {
                        // Closed outlines (such as the WENTADUCT cross-section) are
                        // not centrelines.
                        var closed = e as ZwSoft.ZwCAD.DatabaseServices.Polyline;
                        if (closed != null && closed.Closed) { sel.Skipped++; continue; }
                        var d = new DrawnDuct { Handle = h, Section = tag != null ? tag.Section() : null };
                        List<Wenta.Point2> pts = Vertices(e);
                        if (pts != null) d.PointsMm.AddRange(pts);
                        if (d.PointsMm.Count < 2) { sel.Skipped++; continue; }
                        sel.Ducts.Add(d);
                    }
                    sel.Ids[h] = id;
                    if (tag != null) sel.Tags[h] = tag;
                }
                tr.Commit();
            }
            return sel;
        }

        // Centreline vertices of a LINE, LWPOLYLINE or ARC in drawing units;
        // null for any other entity.
        internal static List<Wenta.Point2> Vertices(Entity e)
        {
            var pts = new List<Wenta.Point2>();
            var line = e as Line;
            var pl = e as ZwSoft.ZwCAD.DatabaseServices.Polyline;
            var arc = e as Arc;
            if (line != null)
            {
                pts.Add(new Wenta.Point2(line.StartPoint.X, line.StartPoint.Y));
                pts.Add(new Wenta.Point2(line.EndPoint.X, line.EndPoint.Y));
            }
            else if (pl != null)
            {
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    pts.Add(new Wenta.Point2(p.X, p.Y));
                    if (i + 1 < pl.NumberOfVertices && Math.Abs(pl.GetBulgeAt(i)) > 1e-12)
                    {
                        double sweep = 4.0 * Math.Atan(Math.Abs(pl.GetBulgeAt(i)));
                        int n = ArcSteps(sweep);
                        for (int k = 1; k < n; k++)
                        {
                            Point3d q = pl.GetPointAtParameter(i + (double)k / n);
                            pts.Add(new Wenta.Point2(q.X, q.Y));
                        }
                    }
                }
            }
            else if (arc != null)
            {
                double sweep = arc.EndAngle - arc.StartAngle;
                if (sweep <= 0.0) sweep += 2.0 * Math.PI;
                int n = ArcSteps(sweep);
                for (int k = 0; k <= n; k++)
                {
                    double a = arc.StartAngle + sweep * k / n;
                    pts.Add(new Wenta.Point2(arc.Center.X + arc.Radius * Math.Cos(a),
                        arc.Center.Y + arc.Radius * Math.Sin(a)));
                }
            }
            else return null;
            return pts;
        }

        // Arcs and polyline bulges are followed as chords of at most 5°, so
        // the traced length is within 0.04 % of the arc length.
        internal static int ArcSteps(double sweepRad)
        {
            return Math.Max(1, (int)Math.Ceiling(sweepRad / (5.0 * Math.PI / 180.0)));
        }

        internal static WentaTag ReadTag(Entity e)
        {
            ResultBuffer rb = e.GetXDataForApplication(WentaTag.AppName);
            if (rb == null) return null;
            var list = new List<KeyValuePair<int, object>>();
            foreach (TypedValue tv in rb.AsArray())
                list.Add(new KeyValuePair<int, object>(tv.TypeCode, tv.Value));
            return WentaTag.FromXData(list);
        }

        internal static void WriteTag(Transaction tr, Database db, Entity e, WentaTag tag)
        {
            EnsureRegApp(tr, db);
            if (string.IsNullOrEmpty(tag.Guid)) tag.Guid = Guid.NewGuid().ToString("D");
            var values = new List<TypedValue> { new TypedValue(1001, WentaTag.AppName) };
            foreach (KeyValuePair<int, object> kv in tag.ToXData())
                values.Add(new TypedValue(kv.Key, kv.Value));
            e.XData = new ResultBuffer(values.ToArray());
        }

        // Existing tag of the entity, upgraded to v2 (legacy WENTADUCT values
        // kept, a GUID assigned), or a fresh tag of the given class.
        internal static WentaTag TagFor(Selection sel, string handle, string wentaClass)
        {
            WentaTag tag;
            if (sel.Tags.TryGetValue(handle, out tag) && tag != null)
            {
                if (tag.Legacy || string.IsNullOrEmpty(tag.Guid))
                {
                    tag.Legacy = false;
                    tag.Guid = Guid.NewGuid().ToString("D");
                }
                if (string.IsNullOrEmpty(tag.Class)) tag.Class = wentaClass;
                return tag;
            }
            tag = WentaTag.New(wentaClass);
            sel.Tags[handle] = tag;
            return tag;
        }

        internal static void EnsureRegApp(Transaction tr, Database db)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(WentaTag.AppName)) return;
            rat.UpgradeOpen();
            var ra = new RegAppTableRecord { Name = WentaTag.AppName };
            rat.Add(ra);
            tr.AddNewlyCreatedDBObject(ra, true);
        }

        internal static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex) };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        internal static BlockTableRecord ModelSpace(Transaction tr, Database db)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        // Updates the text whose handle the tag stores under `key`, or
        // creates it (and records its handle) when missing or erased.
        internal static void UpsertText(Transaction tr, Database db, WentaTag tag, string key,
            string layer, Point3d at, double height, string text)
        {
            DBText existing = null;
            string h = tag.Get(key);
            if (!string.IsNullOrEmpty(h))
            {
                ObjectId id;
                if (TryGetId(db, h, out id) && !id.IsErased)
                    existing = tr.GetObject(id, OpenMode.ForWrite) as DBText;
            }
            if (existing != null)
            {
                existing.TextString = text;
                existing.Position = at;
                return;
            }
            var t = new DBText { Position = at, Height = height, TextString = text, Layer = layer };
            ModelSpace(tr, db).AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            tag.Set(key, t.Handle.ToString());
        }

        internal static bool TryGetId(Database db, string handle, out ObjectId id)
        {
            id = ObjectId.Null;
            long value;
            if (!long.TryParse(handle, NumberStyles.HexNumber, C, out value)) return false;
            return db.TryGetObjectId(new Handle(value), out id);
        }

        // Label anchor: midpoint of the longest segment of the duct, lifted
        // a little off the centreline.
        internal static Point3d LabelPoint(DrawnDuct d, double offsetMm)
        {
            int best = 1;
            double bestLen = -1.0;
            for (int i = 1; i < d.PointsMm.Count; i++)
            {
                double len = d.PointsMm[i - 1].DistanceTo(d.PointsMm[i]);
                if (len > bestLen) { bestLen = len; best = i; }
            }
            Wenta.Point2 a = d.PointsMm[best - 1], b = d.PointsMm[best];
            return new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0 + offsetMm, 0.0);
        }

        internal static void ClearLayer(Transaction tr, Database db, string layer)
        {
            BlockTableRecord ms = ModelSpace(tr, db);
            foreach (ObjectId id in ms)
            {
                Entity e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e != null && e.Layer == layer)
                {
                    e.UpgradeOpen();
                    e.Erase();
                }
            }
        }

        internal static Fluid PromptFluid(Editor ed)
        {
            var o = new PromptDoubleOptions("\nSite altitude [m] <0>: ")
            {
                AllowNone = true, AllowNegative = false, DefaultValue = 0.0, UseDefaultValue = true,
            };
            PromptDoubleResult r = ed.GetDouble(o);
            if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return null;
            double alt = r.Status == PromptStatus.OK ? r.Value : 0.0;
            return alt > 0.0 ? Fluid.AirAtAltitude(alt) : Fluid.StandardAir();
        }

        internal static double? PromptDouble(Editor ed, string message, double def, bool allowZero = false,
            bool allowNegative = false)
        {
            var o = new PromptDoubleOptions(message)
            {
                AllowNone = true, AllowNegative = allowNegative, AllowZero = allowZero,
                DefaultValue = def, UseDefaultValue = true,
            };
            PromptDoubleResult r = ed.GetDouble(o);
            if (r.Status == PromptStatus.None) return def;
            if (r.Status != PromptStatus.OK) return null;
            return r.Value;
        }

        internal static string PromptKeyword(Editor ed, string message, string def, params string[] keywords)
        {
            var o = new PromptKeywordOptions(message) { AllowNone = true };
            foreach (string k in keywords) o.Keywords.Add(k);
            o.Keywords.Default = def;
            PromptResult r = ed.GetKeywords(o);
            if (r.Status == PromptStatus.None) return def;
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrEmpty(r.StringResult) ? def : r.StringResult;
        }

        internal static DrawingTrace Trace(Editor ed, Selection sel)
        {
            DrawingTrace t = DrawingTrace.Build(sel.Ducts, sel.Markers,
                new DrawingTraceOptions { Name = System.IO.Path.GetFileNameWithoutExtension(ed.Document.Name) });
            foreach (string w in t.Warnings) ed.WriteMessage("\nWarning: " + w);
            return t;
        }

        internal static void Lines(Editor ed, string text)
        {
            foreach (string line in text.Split('\n')) ed.WriteMessage("\n" + line);
        }
    }
}
