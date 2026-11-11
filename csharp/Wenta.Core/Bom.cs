using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KnrCodeMap = Wenta.KnrMap;

namespace Wenta
{
    public sealed class Bom
    {
        public sealed class BomRow
        {
            public string ItemId;
            public string Kind;
            public string Description;
            public double Length;
            public double Area;
            public double Flowrate;
            public string KnrCode;
            public string CatalogSource;
        }

        public static readonly Dictionary<string, string> KnrMap =
            new Dictionary<string, string>
        {
            { "duct",        "KNR 2-08 0101 (configure)" },
            { "flex",        "KNR 2-08 0201 (configure)" },
            { "fitting",     "KNR 2-08 0301 (configure)" },
            { "terminal",    "KNR 2-08 0401 (configure)" },
            { "source",      "" },
        };

        public readonly List<BomRow> Rows = new List<BomRow>();

        public KnrCodeMap Mapping;

        public readonly List<string> Unmapped = new List<string>();

        public double TotalLength
        {
            get
            {
                double t = 0.0;
                foreach (BomRow r in Rows) t += r.Length;
                return t;
            }
        }

        public double TotalArea
        {
            get
            {
                double t = 0.0;
                foreach (BomRow r in Rows) t += r.Area;
                return t;
            }
        }

        public static Bom Build(Network network)
        {
            return Build(network, null);
        }

        public static Bom Build(Network network, KnrCodeMap map)
        {
            var bom = new Bom();
            bom.Mapping = map ?? KnrCodeMap.Default();
            foreach (var kv in network.Components)
            {
                Component c = kv.Value;
                var row = new BomRow { ItemId = kv.Key };
                double? flow = null;
                foreach (Port p in c.Ports)
                    if (p.Flowrate != null) { flow = p.Flowrate; break; }
                row.Flowrate = flow ?? 0.0;

                string shape = null;
                string type = null;
                RigidDuct duct = c as RigidDuct;
                if (duct != null)
                {
                    row.Kind = "duct";
                    row.Description = duct.CrossSection.Describe();
                    row.Length = duct.Length;
                    row.Area = duct.CrossSection.Area * duct.Length;
                    shape = ShapeOf(duct.CrossSection);
                }
                else if (c is FlexDuct)
                {
                    var fd = (FlexDuct)c;
                    row.Kind = "flex";
                    row.Description = "flex D=" + (fd.Diameter * 1000).ToString(CultureInfo.InvariantCulture) + "mm";
                    row.Length = fd.Length;
                    row.Area = fd.Area * fd.Length;
                    shape = Sizing.ShapeRound;
                }
                else if (c is Terminal)
                {
                    row.Kind = "terminal";
                    row.Description = "terminal " + ((Terminal)c).Flowrate.ToString(CultureInfo.InvariantCulture) + " m3/s";
                }
                else if (c is Source)
                {
                    row.Kind = "source";
                    row.Description = "source";
                }
                else if (c is Tee)
                {
                    row.Kind = "fitting";
                    row.Description = "tee (straight z=" + ((Tee)c).ZetaStraight.ToString(CultureInfo.InvariantCulture)
                        + ", branch z=" + ((Tee)c).ZetaBranch.ToString(CultureInfo.InvariantCulture) + ")";
                    shape = ShapeOf(((Tee)c).CrossSection);
                    type = "tee";
                }
                else if (c is TwoPortFitting)
                {
                    row.Kind = "fitting";
                    row.Description = "in-line fitting z=" + ((TwoPortFitting)c).Zeta.ToString(CultureInfo.InvariantCulture);
                    shape = ShapeOf(((TwoPortFitting)c).CrossSection);
                    type = "inline";
                }
                else
                {
                    row.Kind = "fitting";
                    row.Description = c.GetType().Name;
                    type = c.GetType().Name;
                }
                string knr = bom.Mapping.CodeFor(row.Kind, shape, type);
                if (knr == null)
                {
                    knr = "";
                    string query = KnrCodeMap.QueryKey(row.Kind, shape, type);
                    if (!bom.Unmapped.Contains(query)) bom.Unmapped.Add(query);
                }
                row.KnrCode = knr;
                bom.Rows.Add(row);
            }
            return bom;
        }

        private static string ShapeOf(CrossSection cs)
        {
            if (cs is Round) return Sizing.ShapeRound;
            if (cs is Rectangular) return Sizing.ShapeRectangular;
            return null;
        }

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("item;kind;description;length_m;area_m2;flow_m3s;knr_code");
            foreach (BomRow r in Rows)
            {
                sb.Append(r.ItemId).Append(';')
                  .Append(r.Kind).Append(';')
                  .Append(r.Description).Append(';')
                  .Append(r.Length.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                  .Append(r.Area.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                  .Append(r.Flowrate.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                  .Append(r.KnrCode).AppendLine();
            }
            return sb.ToString();
        }
    }
}
