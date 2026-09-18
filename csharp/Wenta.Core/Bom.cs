using System;
using System.Collections.Generic;
using System.Text;
// The static field Bom.KnrMap shadows the type name inside this class.
using KnrCodeMap = Wenta.KnrMap;

namespace Wenta
{
    /// <summary>Bill of materials from a network — with KNR-ready rows.
    /// (PL-market item: competitive answer to Wentyle's KNR schedules.)
    ///
    /// KNR codes are *configurable per project*: the defaults below are
    /// placeholders pointing at the right KNR section for ventilation
    /// systems. For a real edition pass a <see cref="Wenta.KnrMap"/> loaded
    /// from JSON to <see cref="Build(Network, Wenta.KnrMap)"/> (issue #54);
    /// kinds the map does not cover get an empty code and are listed in
    /// <see cref="Unmapped"/> — never a made-up code.</summary>
    public sealed class Bom
    {
        public sealed class BomRow
        {
            public string ItemId;          // component id in the network
            public string Kind;           // duct | flex | fitting | terminal | source
            public string Description;   // e.g. "rect 400x200 mm"
            public double Length;         // [m], ducts/flex only
            public double Area;            // [m^2], ducts only
            public double Flowrate;        // [m^3/s] after solve
            public string KnrCode;        // estimate code
            public string CatalogSource;  // provenance for fitting zeta, if any
        }

        /// <summary>Default KNR estimate codes (placeholders — configure
        /// per local KNR edition before using for real estimates).</summary>
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

        /// <summary>The KNR mapping the rows were coded with
        /// (<see cref="Wenta.KnrMap.Default"/> unless one was passed to
        /// <see cref="Build(Network, Wenta.KnrMap)"/>).</summary>
        public KnrCodeMap Mapping;

        /// <summary>Distinct <c>kind [shape=…] [type=…]</c> queries that
        /// <see cref="Mapping"/> had no code for, in row order; those rows
        /// carry an empty <see cref="BomRow.KnrCode"/>. Empty under the
        /// default map.</summary>
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

        /// <summary>Build a BOM from a (solved) network with the default
        /// (placeholder) KNR codes.</summary>
        public static Bom Build(Network network)
        {
            return Build(network, null);
        }

        /// <summary>Build a BOM from a (solved) network, coding rows with
        /// <paramref name="map"/> (null = <see cref="Wenta.KnrMap.Default"/>).
        /// Rows the map cannot code get <c>KnrCode == ""</c> and their query
        /// is added to <see cref="Unmapped"/> (and to the map's own list).</summary>
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

                string shape = null;   // round | rectangular, when the item has a cross-section
                string type = null;    // fitting sub-type, fittings only
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
                    row.Description = "flex D=" + (fd.Diameter * 1000) + "mm";
                    row.Length = fd.Length;
                    // flex is round: surface area = π·(D/2)²·L (matches venti bom.rs)
                    row.Area = fd.Area * fd.Length;
                    shape = Sizing.ShapeRound;
                }
                else if (c is Terminal)
                {
                    row.Kind = "terminal";
                    row.Description = "terminal " + ((Terminal)c).Flowrate + " m3/s";
                }
                else if (c is Source)
                {
                    row.Kind = "source";
                    row.Description = "source";
                }
                else if (c is Tee)
                {
                    row.Kind = "fitting";
                    row.Description = "tee (straight z=" + ((Tee)c).ZetaStraight
                        + ", branch z=" + ((Tee)c).ZetaBranch + ")";
                    shape = ShapeOf(((Tee)c).CrossSection);
                    type = "tee";
                }
                else if (c is TwoPortFitting)
                {
                    row.Kind = "fitting";
                    row.Description = "in-line fitting z=" + ((TwoPortFitting)c).Zeta;
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

        /// <summary>"round" / "rectangular" for the two cross-sections; null
        /// for an unknown subclass (no override can then match on shape).</summary>
        private static string ShapeOf(CrossSection cs)
        {
            if (cs is Round) return Sizing.ShapeRound;
            if (cs is Rectangular) return Sizing.ShapeRectangular;
            return null;
        }

        /// <summary>CSV export (semicolon-separated, decimal point). Columns:
        /// item;kind;description;length_m;area_m2;flow_m3s;knr_code.</summary>
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
