// ----------------------------------------------------------------------------
// MakeCuix — assemble Wenta.CUIX, a partial CUIX package for ZWCAD 2021 that
// adds a 'Wenta' ribbon tab with buttons. Modeled 1:1 on ZWSOFT's own
// APP+.cuix partial package (same XML schema, same package layout).
//
// C# port of the former make_cuix.py (issue #64: C#-only repo). Produces the
// same part names, the same XML strings, the same PNG pixels and the same
// STORED (uncompressed) zip method. Dependency-free: the PNG writer hand-rolls
// the IHDR/IDAT/IEND chunks, the zlib wrapper, CRC-32 and Adler-32, and the
// zip container is written by hand too — .NET Framework's ZipArchive emits
// CompressionLevel.NoCompression entries as method 8 (Deflate), not method 0
// (STORED), which is what ZWSOFT's own CUIX packages use.
//
// Usage: MakeCuix.exe [outdir]      (default: bin)  ->  <outdir>\Wenta.CUIX
// Built by zwcad-plugin\build.cmd with the bare csc toolchain; references
// System.dll (DeflateStream) and System.Core.dll only.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WentaZwcad.Tools
{
    internal static class MakeCuix
    {
        // --------------------------------------------------------------------
        // 1. tiny PNG writer (no dependencies)
        // --------------------------------------------------------------------

        private struct Px
        {
            public readonly byte R, G, B, A;
            public Px(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        }

        private static readonly Px Teal = new Px(0, 128, 128, 255);
        private static readonly Px TealDark = new Px(0, 84, 84, 255);
        private static readonly Px Gray = new Px(96, 110, 128, 255);
        private static readonly Px White = new Px(255, 255, 255, 255);
        private static readonly Px Bg = new Px(255, 255, 255, 0);   // transparent background

        private static readonly uint[] CrcTable = BuildCrcTable();

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

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteBE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        /// <summary>PNG chunk: big-endian length, tag, data, CRC-32 over tag+data.</summary>
        private static void Chunk(Stream s, string tag, byte[] data)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
            WriteBE(s, (uint)data.Length);
            s.Write(tagBytes, 0, tagBytes.Length);
            s.Write(data, 0, data.Length);
            WriteBE(s, Crc32(Concat(tagBytes, data)));
        }

        /// <summary>zlib stream: 2-byte header (deflate, 32K window, max level),
        /// raw deflate body, big-endian Adler-32 of the uncompressed data.</summary>
        private static byte[] ZlibCompress(byte[] raw)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0xDA);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
                ds.Write(raw, 0, raw.Length);
            WriteBE(ms, Adler32(raw));
            return ms.ToArray();
        }

        /// <summary>8-bit RGBA, non-interlaced, filter type 0 on every scanline.</summary>
        private static byte[] MakePng(int width, int height, Px[,] pixels)
        {
            var raw = new byte[height * (1 + 4 * width)];
            int o = 0;
            for (int r = 0; r < height; r++)
            {
                raw[o++] = 0;                                   // filter: None
                for (int c = 0; c < width; c++)
                {
                    Px p = pixels[r, c];
                    raw[o++] = p.R; raw[o++] = p.G; raw[o++] = p.B; raw[o++] = p.A;
                }
            }

            var ihdr = new MemoryStream();
            WriteBE(ihdr, (uint)width);
            WriteBE(ihdr, (uint)height);
            ihdr.WriteByte(8);      // bit depth
            ihdr.WriteByte(6);      // colour type: RGBA
            ihdr.WriteByte(0);      // compression
            ihdr.WriteByte(0);      // filter
            ihdr.WriteByte(0);      // interlace

            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            Chunk(png, "IHDR", ihdr.ToArray());
            Chunk(png, "IDAT", ZlibCompress(raw));
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        private static Px[,] Blank(int size)
        {
            var px = new Px[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    px[r, c] = Bg;
            return px;
        }

        /// <summary>Duct cross-section: teal rounded-ish rectangle outline + airflow line.</summary>
        private static byte[] IconDuct(int size = 32)
        {
            Px[,] px = Blank(size);
            int m = 5;                      // margin
            int w = size - 2 * m;           // inner square size
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    bool edge = ((r == m || r == m + w) && m <= c && c <= m + w) ||
                                ((c == m || c == m + w) && m <= r && r <= m + w);
                    if (edge) px[r, c] = Teal;
                }
            // airflow: horizontal line through the middle
            int mid = size / 2;
            for (int c = m + 4; c < m + w - 3; c++)
                px[mid, c] = TealDark;
            return MakePng(size, size, px);
        }

        /// <summary>Palette panel: window with title bar.</summary>
        private static byte[] IconPanel(int size = 32)
        {
            Px[,] px = Blank(size);
            int m = 5, w = size - 2 * 5;
            for (int r = m; r <= m + w; r++)
                for (int c = m; c <= m + w; c++)
                {
                    bool edge = r == m || r == m + w || c == m || c == m + w;
                    bool title = m < r && r <= m + 5 && m < c && c < m + w;
                    if (edge) px[r, c] = Gray;
                    else if (title) px[r, c] = Teal;
                }
            return MakePng(size, size, px);
        }

        /// <summary>Info: teal circle with a white 'i'.</summary>
        private static byte[] IconInfo(int size = 32)
        {
            Px[,] px = Blank(size);
            double cx = (size - 1) / 2.0, cy = cx;
            double rad = size / 2.0 - 2;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    double d2 = (r - cx) * (r - cx) + (c - cy) * (c - cy);
                    if (d2 <= rad * rad) px[r, c] = Teal;
                }
            // the 'i': dot at (cy-5), stem cy-2..cy+5
            foreach (int c in new[] { 14, 15, 16 })
                px[10, c] = px[11, c] = White;              // dot
            for (int r = 14; r < 21; r++)
                px[r, 15] = White;                          // stem
            return MakePng(size, size, px);
        }

        // --------------------------------------------------------------------
        // 2. CUI XML parts
        // --------------------------------------------------------------------

        /// <summary>The source file may be checked out with CRLF; the package
        /// parts always use LF (as the Python tool's string literals did).</summary>
        private static string Lf(string s) { return s.Replace("\r\n", "\n"); }

        private const string HeaderCui = @"<?xml version=""1.0""?>
<CustSection xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <FileVersion MajorVersion=""0"" MinorVersion=""6"" IncrementalVersion=""1"" UserVersion=""1"" />
  <Header>
    <CommonConfiguration>
      <CommonItems>
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      </CommonItems>
    </CommonConfiguration>
  </Header>
</CustSection>";

        private const string MenuGroupCui = @"<?xml version=""1.0""?>
<MenuGroup xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" Name=""Wenta"" DisplayName=""Wenta"">
  <MacroGroup Name=""WentaMacros"">
    <MenuMacro UID=""WENTA_DUCT"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_DUCT_NM"">Duct Section</Name>
        <Command>^C^C_.WENTADUCT</Command>
        <HelpString xlate=""true"" UID=""WENTA_DUCT_HS"">Draw a rectangular duct cross-section and label it: WENTADUCT</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_duct_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_PANEL"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_PANEL_NM"">Wenta Panel</Name>
        <Command>^C^C_.WENTAPANEL</Command>
        <HelpString xlate=""true"" UID=""WENTA_PANEL_HS"">Show the dockable Wenta palette panel: WENTAPANEL</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_INFO"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_INFO_NM"">Plugin Info</Name>
        <Command>^C^C_.WENTAHELLO</Command>
        <HelpString xlate=""true"" UID=""WENTA_INFO_HS"">Show Wenta plugin info: WENTAHELLO</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_info_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_CUCT"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_CUCT_NM"">Fitting Catalog</Name>
        <Command>^C^C_.WENTACATALOG</Command>
        <HelpString xlate=""true"" UID=""WENTA_CUCT_HS"">Load the open zeta-catalog and show a lookup: WENTACATALOG</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_info_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_BOM"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_BOM_NM"">BOM + KNR</Name>
        <Command>^C^C_.WENTABOM</Command>
        <HelpString xlate=""true"" UID=""WENTA_BOM_HS"">Solve the reference network and export the BOM with KNR rows: WENTABOM</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
  </MacroGroup>
</MenuGroup>";

        private const string RibbonRootCui = @"<?xml version=""1.0""?>
<RibbonRoot>
  <RibbonPanelSourceCollection xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
    <RibbonPanelSource UID=""WENTA_RBPS"" Text=""Wenta"" HiddenInEditor=""false"" KeyTip=""WE"">
      <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      <Alias>ID_WentaPanel</Alias>
      <Name xlate=""true"" UID=""WENTA_RBPS_NM"">Wenta</Name>
      <DialogBoxLauncher UID=""WENTA_RBPS_DBLR"" CommandID="""" CommandType=""Macro"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      </DialogBoxLauncher>
      <RibbonRow UID=""WENTA_RBRW"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <RibbonCommandButton UID=""WENTA_RBTN_DUCT"" Id=""ZwRibbonCommandButton"" Text=""Duct Section"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_DUCT"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_DUCT_TT"">Duct Section</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_PANEL"" Id=""ZwRibbonCommandButton"" Text=""Wenta Panel"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_PANEL"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_PANEL_TT"">Wenta Panel</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_INFO"" Id=""ZwRibbonCommandButton"" Text=""Plugin Info"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_INFO"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_INFO_TT"">Plugin Info</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_CUCT"" Id=""ZwRibbonCommandButton"" Text=""Fitting Catalog"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_CUCT"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_CUCT_TT"">Fitting Catalog</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_BOM"" Id=""ZwRibbonCommandButton"" Text=""BOM + KNR"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_BOM"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_BOM_TT"">BOM + KNR</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
      </RibbonRow>
      <RibbonPanelBreak UID=""WENTA_RPBRK"" Id=""ZwRibbonPanelBreak"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      </RibbonPanelBreak>
    </RibbonPanelSource>
  </RibbonPanelSourceCollection>
  <RibbonTabSourceCollection>
    <RibbonTabSource Text=""Wenta"" UID=""WENTA_RBTS"" DisplayType=""Full"" DefaultDisplay=""AddToWorkSpace"" WorkspaceBehavior=""MergeOrAddTab"">
      <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      <Name xlate=""true"" UID=""WENTA_RBTS_NM"">Wenta</Name>
      <RibbonPanelSourceReference UID=""WENTA_RBPSREF"" PanelId=""WENTA_RBPS"" ResizeStyle=""Default"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
      </RibbonPanelSourceReference>
    </RibbonTabSource>
  </RibbonTabSourceCollection>
</RibbonRoot>";

        // Ordered: order matters for the zip, _rels/.rels and Menu_Package_Info.xml.
        private static readonly KeyValuePair<string, string>[] Stubs =
        {
            new KeyValuePair<string, string>("AcceleratorRoot.cui", @"<AcceleratorRoot xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("ImageMenuRoot.cui", @"<ImageMenuRoot xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("LSPFiles.cui", @"<LSPFiles xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("PopMenuRoot.cui", @"<PopMenuRoot xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("PopMenuRoot_Documentless.cui", @"<PopMenuRoot_Documentless xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("QuickAccessToolbarRoot.cui", @"<QuickAccessToolbarRoot xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" />"),
            new KeyValuePair<string, string>("ToolbarRoot.cui", @"<ToolbarRoot xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" />"),
            new KeyValuePair<string, string>("WorkspaceRoot.cui", "<WorkspaceRoot xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n  <WorkspaceConfigRoot />\n</WorkspaceRoot>"),
        };

        private const string ContentTypes =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"png\" ContentType=\"text/xml\" />" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
            "<Default Extension=\"cui\" ContentType=\"text/xml\" />" +
            "<Default Extension=\"xml\" ContentType=\"text/xml\" />" +
            "</Types>";

        private static readonly KeyValuePair<string, byte[]>[] Images =
        {
            new KeyValuePair<string, byte[]>("wenta_duct_32.png", IconDuct()),
            new KeyValuePair<string, byte[]>("wenta_panel_32.png", IconPanel()),
            new KeyValuePair<string, byte[]>("wenta_info_32.png", IconInfo()),
        };

        // NB: WorkspaceRoot.cui is listed here *and* in Stubs, so (exactly as
        // the Python tool did) it appears twice in .rels and in the package
        // info, but is written to the zip only once.
        private static readonly string[] MainParts =
            { "Header.cui", "WorkspaceRoot.cui", "MenuGroup.cui", "RibbonRoot.cui" };

        private static string RelsXml()
        {
            var sb = new StringBuilder();
            int i = 0;
            Action<string, string> rel = (target, rtype) =>
            {
                i++;
                sb.Append("<Relationship Type=\"").Append(rtype)
                  .Append("\" Target=\"/").Append(target)
                  .Append("\" Id=\"R").Append(i.ToString("x16")).Append("\" />");
            };

            foreach (string part in MainParts) rel(part, "CUI");
            foreach (var stub in Stubs) rel(stub.Key, "CUI");
            rel("Menu_Package_Info.xml", "CUI");
            foreach (var img in Images) rel(img.Key, "Image");

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   sb + "</Relationships>";
        }

        private static string PackageInfo()
        {
            // Mirrors Python's datetime.now().isoformat() (microsecond precision).
            string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.ffffff");
            var parts = new List<string>(MainParts) { "Menu_Package_Info.xml" };
            foreach (var stub in Stubs) parts.Add(stub.Key);
            foreach (var img in Images) parts.Add(img.Key);

            var rows = new List<string>();
            foreach (string p in parts)
                rows.Add("  <PartData PartData_Name=\"/" + p + "\" PartData_Modified=\"" + now + "\" />");

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                   "<MenuPackageParts>\n" + string.Join("\n", rows) + "\n</MenuPackageParts>";
        }

        // --------------------------------------------------------------------
        // 3. package it (STORED method, like ZWSOFT's own CUIX)
        // --------------------------------------------------------------------

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Minimal zip writer: every entry STORED (method 0), no zip64,
        /// no data descriptors, no extra fields. Field values mirror what Python's
        /// zipfile.writestr produced (version 2.0, external attr 0o600 &lt;&lt; 16).</summary>
        private sealed class StoredZipWriter : IDisposable
        {
            private readonly Stream _out;
            private readonly List<byte[]> _central = new List<byte[]>();

            public StoredZipWriter(Stream output) { _out = output; }

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

        private static void AddEntry(StoredZipWriter zip, string name, string text)
        {
            zip.Add(name, Utf8NoBom.GetBytes(text));
        }

        private static int Main(string[] args)
        {
            try
            {
                string outDir = args.Length > 0 ? args[0] : "bin";
                string outPath = Path.Combine(outDir, "Wenta.CUIX");
                Directory.CreateDirectory(outDir);

                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                using (var zip = new StoredZipWriter(fs))
                {
                    AddEntry(zip, "[Content_Types].xml", ContentTypes);
                    AddEntry(zip, "_rels/.rels", RelsXml());
                    AddEntry(zip, "Menu_Package_Info.xml", PackageInfo());
                    AddEntry(zip, "Header.cui", Lf(HeaderCui));
                    AddEntry(zip, "MenuGroup.cui", Lf(MenuGroupCui));
                    AddEntry(zip, "RibbonRoot.cui", Lf(RibbonRootCui));
                    foreach (var stub in Stubs)
                        AddEntry(zip, stub.Key, "<?xml version=\"1.0\"?>\n" + Lf(stub.Value));
                    foreach (var img in Images)
                        zip.Add(img.Key, img.Value);
                }

                Console.WriteLine("Wenta.CUIX written: {0} ({1} bytes)", outPath, new FileInfo(outPath).Length);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MakeCuix failed: " + ex.Message);
                return 1;
            }
        }
    }
}
