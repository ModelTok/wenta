using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Wenta
{
    /// <summary>JSON and XLSX export of a <see cref="Bom"/> (issue #29,
    /// library half; drawing tables are plugin work). Same toolchain as
    /// <see cref="NetworkJson"/>: <c>JavaScriptSerializer</c> over a
    /// Dictionary/ArrayList tree for JSON, and a dependency-free OOXML
    /// package (hand-rolled STORED zip, inline-string cells) for XLSX —
    /// the build references System.dll / System.Core.dll /
    /// System.Web.Extensions.dll only.
    ///
    /// JSON format (snake_case, schema_version 1; keys mirror the
    /// <see cref="Bom.BomRow"/> fields; metres, m², m³/s):
    /// {
    ///   "schema_version": 1,
    ///   "network": "bom-tee",            // null when the caller gave no name
    ///   "rows": [
    ///     { "item_id": "duct", "kind": "duct", "description": "round D=315mm",
    ///       "length": 20, "area": 1.558…, "flowrate": 0.1,
    ///       "knr_code": "KNR 2-08 0101 (configure)", "catalog_source": null }, …
    ///   ],
    ///   "totals": { "row_count": 7, "length": 28, "area": 1.67… }
    /// }
    ///
    /// XLSX layout: one sheet; row 1 = header, rows 2..N+1 = BOM rows in
    /// <see cref="Bom.Rows"/> order, last row = totals ("TOTAL", row count
    /// under Kind, sum of length and area). Text cells are inline strings
    /// (<c>t="inlineStr"</c>, so no sharedStrings part); numeric cells are
    /// plain <c>&lt;v&gt;</c> values written with the invariant "R" format.</summary>
    public static class BomExport
    {
        /// <summary>Highest <c>schema_version</c> emitted by <see cref="ToJson"/>.</summary>
        public const int SchemaVersion = 1;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Column headers shared by the XLSX sheet; the JSON keys are
        /// the snake_case field names (see class summary).</summary>
        private static readonly string[] Headers =
        {
            "Item", "Kind", "Description", "Length [m]", "Area [m2]",
            "Flow [m3/s]", "KNR code", "Catalog source",
        };

        // ---- JSON ------------------------------------------------------------

        /// <summary>Serialise <paramref name="bom"/> to JSON.
        /// <paramref name="networkName"/> is optional (the BOM itself does not
        /// remember its network); the <c>network</c> key is <c>null</c> when
        /// it is not supplied.</summary>
        public static string ToJson(Bom bom, string networkName = null)
        {
            if (bom == null)
                throw new WentaException("BomExport.ToJson: bom is null");

            var root = new Dictionary<string, object>();
            root["schema_version"] = SchemaVersion;
            root["network"] = networkName;

            var rows = new ArrayList();
            foreach (Bom.BomRow r in bom.Rows)
            {
                var d = new Dictionary<string, object>();
                d["item_id"] = r.ItemId;
                d["kind"] = r.Kind;
                d["description"] = r.Description;
                d["length"] = r.Length;
                d["area"] = r.Area;
                d["flowrate"] = r.Flowrate;
                d["knr_code"] = r.KnrCode;
                d["catalog_source"] = r.CatalogSource;
                rows.Add(d);
            }
            root["rows"] = rows;

            var totals = new Dictionary<string, object>();
            totals["row_count"] = bom.Rows.Count;
            totals["length"] = bom.TotalLength;
            totals["area"] = bom.TotalArea;
            root["totals"] = totals;

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Serialize(root);
        }

        /// <summary>Write <see cref="ToJson"/> output to <paramref name="path"/> (UTF-8, no BOM).</summary>
        public static void SaveJson(Bom bom, string path, string networkName = null)
        {
            File.WriteAllText(path, ToJson(bom, networkName), Utf8NoBom);
        }

        // ---- XLSX ------------------------------------------------------------

        /// <summary>Build a minimal valid .xlsx package in memory. See the class
        /// summary for the sheet layout. <paramref name="sheetName"/> is made
        /// Excel-legal (max 31 chars, none of <c>: \ / ? * [ ]</c>).</summary>
        public static byte[] ToXlsx(Bom bom, string sheetName = "BOM")
        {
            if (bom == null)
                throw new WentaException("BomExport.ToXlsx: bom is null");

            string sheet = SanitiseSheetName(sheetName);

            var ms = new MemoryStream();
            using (var zip = new StoredZipWriter(ms))
            {
                zip.Add("[Content_Types].xml", Utf8NoBom.GetBytes(ContentTypesXml()));
                zip.Add("_rels/.rels", Utf8NoBom.GetBytes(RootRelsXml()));
                zip.Add("xl/workbook.xml", Utf8NoBom.GetBytes(WorkbookXml(sheet)));
                zip.Add("xl/_rels/workbook.xml.rels", Utf8NoBom.GetBytes(WorkbookRelsXml()));
                zip.Add("xl/worksheets/sheet1.xml", Utf8NoBom.GetBytes(SheetXml(bom)));
            }
            return ms.ToArray();
        }

        /// <summary>Write <see cref="ToXlsx"/> output to <paramref name="path"/>.</summary>
        public static void SaveXlsx(Bom bom, string path)
        {
            File.WriteAllBytes(path, ToXlsx(bom));
        }

        private static string SanitiseSheetName(string name)
        {
            if (name == null) name = "";
            var sb = new StringBuilder();
            foreach (char ch in name)
            {
                bool bad = ch == ':' || ch == '\\' || ch == '/' || ch == '?'
                        || ch == '*' || ch == '[' || ch == ']' || ch < 0x20;
                sb.Append(bad ? '_' : ch);
            }
            string s = sb.ToString().Trim();
            if (s.Length == 0) s = "BOM";
            if (s.Length > 31) s = s.Substring(0, 31);
            // Excel rejects a leading/trailing apostrophe.
            s = s.Trim('\'');
            return s.Length == 0 ? "BOM" : s;
        }

        private const string XmlDecl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

        private static string ContentTypesXml()
        {
            return XmlDecl +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>";
        }

        private static string RootRelsXml()
        {
            return XmlDecl +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>";
        }

        private static string WorkbookXml(string sheetName)
        {
            return XmlDecl +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>";
        }

        private static string WorkbookRelsXml()
        {
            return XmlDecl +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>";
        }

        private static string SheetXml(Bom bom)
        {
            int nRows = bom.Rows.Count + 2;                 // header + rows + totals
            string lastCell = ColumnName(Headers.Length - 1) + nRows.ToString(Inv);

            var sb = new StringBuilder();
            sb.Append(XmlDecl);
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<dimension ref=\"A1:").Append(lastCell).Append("\"/>");
            sb.Append("<sheetData>");

            int rowNo = 1;
            sb.Append("<row r=\"1\">");
            for (int c = 0; c < Headers.Length; c++)
                TextCell(sb, c, rowNo, Headers[c]);
            sb.Append("</row>");

            foreach (Bom.BomRow r in bom.Rows)
            {
                rowNo++;
                sb.Append("<row r=\"").Append(rowNo.ToString(Inv)).Append("\">");
                TextCell(sb, 0, rowNo, r.ItemId);
                TextCell(sb, 1, rowNo, r.Kind);
                TextCell(sb, 2, rowNo, r.Description);
                NumberCell(sb, 3, rowNo, r.Length);
                NumberCell(sb, 4, rowNo, r.Area);
                NumberCell(sb, 5, rowNo, r.Flowrate);
                TextCell(sb, 6, rowNo, r.KnrCode);
                TextCell(sb, 7, rowNo, r.CatalogSource);
                sb.Append("</row>");
            }

            rowNo++;
            sb.Append("<row r=\"").Append(rowNo.ToString(Inv)).Append("\">");
            TextCell(sb, 0, rowNo, "TOTAL");
            NumberCell(sb, 1, rowNo, bom.Rows.Count);
            NumberCell(sb, 3, rowNo, bom.TotalLength);
            NumberCell(sb, 4, rowNo, bom.TotalArea);
            sb.Append("</row>");

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void TextCell(StringBuilder sb, int col, int row, string text)
        {
            if (text == null) return;                       // blank cell: omit
            sb.Append("<c r=\"").Append(ColumnName(col)).Append(row.ToString(Inv))
              .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(XmlEscape(text))
              .Append("</t></is></c>");
        }

        private static void NumberCell(StringBuilder sb, int col, int row, double v)
        {
            string cellRef = ColumnName(col) + row.ToString(Inv);
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                // Not representable as an xlsx number; keep the value visible.
                TextCell(sb, col, row, v.ToString("R", Inv));
                return;
            }
            sb.Append("<c r=\"").Append(cellRef).Append("\"><v>")
              .Append(v.ToString("R", Inv))
              .Append("</v></c>");
        }

        /// <summary>0 → "A", 25 → "Z", 26 → "AA", …</summary>
        private static string ColumnName(int index)
        {
            var sb = new StringBuilder();
            int n = index;
            do
            {
                sb.Insert(0, (char)('A' + n % 26));
                n = n / 26 - 1;
            } while (n >= 0);
            return sb.ToString();
        }

        /// <summary>Escape for XML text/attribute content; characters that are
        /// illegal in XML 1.0 (control codes other than tab/LF/CR) are dropped.</summary>
        private static string XmlEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:
                        if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') break;
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        // ---- zip container -------------------------------------------------

        /// <summary>Minimal zip writer: every entry STORED (method 0), no zip64,
        /// no data descriptors, no extra fields. Ported from
        /// zwcad-plugin/tools/MakeCuix.cs so the library needs no
        /// System.IO.Compression reference.</summary>
        private sealed class StoredZipWriter : IDisposable
        {
            private static readonly uint[] CrcTable = BuildCrcTable();

            private readonly Stream _out;
            private readonly List<byte[]> _central = new List<byte[]>();

            public StoredZipWriter(Stream output) { _out = output; }

            private static uint[] BuildCrcTable()
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                return table;
            }

            private static uint Crc32(byte[] data)
            {
                uint c = 0xFFFFFFFFu;
                for (int i = 0; i < data.Length; i++)
                    c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }

            private static void LE16(Stream s, int v)
            {
                s.WriteByte((byte)v);
                s.WriteByte((byte)(v >> 8));
            }

            private static void LE32(Stream s, uint v)
            {
                s.WriteByte((byte)v);
                s.WriteByte((byte)(v >> 8));
                s.WriteByte((byte)(v >> 16));
                s.WriteByte((byte)(v >> 24));
            }

            public void Add(string name, byte[] data)
            {
                byte[] nameBytes = Utf8NoBom.GetBytes(name);   // all part names are ASCII
                uint crc = Crc32(data);
                uint size = (uint)data.Length;
                uint offset = (uint)_out.Position;

                DateTime t = DateTime.Now;
                int dosTime = (t.Hour << 11) | (t.Minute << 5) | (t.Second / 2);
                int dosDate = ((t.Year - 1980) << 9) | (t.Month << 5) | t.Day;

                // local file header
                LE32(_out, 0x04034B50);
                LE16(_out, 20);                 // version needed to extract: 2.0
                LE16(_out, 0);                  // general purpose flags
                LE16(_out, 0);                  // method: STORED
                LE16(_out, dosTime);
                LE16(_out, dosDate);
                LE32(_out, crc);
                LE32(_out, size);               // compressed size
                LE32(_out, size);               // uncompressed size
                LE16(_out, nameBytes.Length);
                LE16(_out, 0);                  // extra field length
                _out.Write(nameBytes, 0, nameBytes.Length);
                _out.Write(data, 0, data.Length);

                // central directory record (written at the end)
                var cd = new MemoryStream();
                LE32(cd, 0x02014B50);
                LE16(cd, 20);                   // version made by: 2.0, MS-DOS
                LE16(cd, 20);                   // version needed to extract
                LE16(cd, 0);                    // flags
                LE16(cd, 0);                    // method: STORED
                LE16(cd, dosTime);
                LE16(cd, dosDate);
                LE32(cd, crc);
                LE32(cd, size);
                LE32(cd, size);
                LE16(cd, nameBytes.Length);
                LE16(cd, 0);                    // extra field length
                LE16(cd, 0);                    // comment length
                LE16(cd, 0);                    // disk number start
                LE16(cd, 0);                    // internal attributes
                LE32(cd, 0x01800000u);          // external attributes: 0o600 << 16
                LE32(cd, offset);               // local header offset
                cd.Write(nameBytes, 0, nameBytes.Length);
                _central.Add(cd.ToArray());
            }

            public void Dispose()
            {
                uint cdStart = (uint)_out.Position;
                foreach (byte[] rec in _central)
                    _out.Write(rec, 0, rec.Length);
                uint cdSize = (uint)_out.Position - cdStart;

                // end of central directory
                LE32(_out, 0x06054B50);
                LE16(_out, 0);                  // this disk
                LE16(_out, 0);                  // disk with central directory
                LE16(_out, _central.Count);     // entries on this disk
                LE16(_out, _central.Count);     // entries total
                LE32(_out, cdSize);
                LE32(_out, cdStart);
                LE16(_out, 0);                  // comment length
                _out.Flush();
            }
        }
    }
}
