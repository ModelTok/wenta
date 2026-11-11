using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WentaZwcad.Tools
{
    internal static class MakeCuix
    {

        private struct Px
        {
            public readonly byte R, G, B, A;
            public Px(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        }

        private static readonly Px Teal = new Px(0, 128, 128, 255);
        private static readonly Px TealDark = new Px(0, 84, 84, 255);
        private static readonly Px Gray = new Px(96, 110, 128, 255);
        private static readonly Px White = new Px(255, 255, 255, 255);
        private static readonly Px Bg = new Px(255, 255, 255, 0);

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

        private static void Chunk(Stream s, string tag, byte[] data)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
            WriteBE(s, (uint)data.Length);
            s.Write(tagBytes, 0, tagBytes.Length);
            s.Write(data, 0, data.Length);
            WriteBE(s, Crc32(Concat(tagBytes, data)));
        }

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

        private static byte[] MakePng(int width, int height, Px[,] pixels)
        {
            var raw = new byte[height * (1 + 4 * width)];
            int o = 0;
            for (int r = 0; r < height; r++)
            {
                raw[o++] = 0;
                for (int c = 0; c < width; c++)
                {
                    Px p = pixels[r, c];
                    raw[o++] = p.R; raw[o++] = p.G; raw[o++] = p.B; raw[o++] = p.A;
                }
            }

            var ihdr = new MemoryStream();
            WriteBE(ihdr, (uint)width);
            WriteBE(ihdr, (uint)height);
            ihdr.WriteByte(8);
            ihdr.WriteByte(6);
            ihdr.WriteByte(0);
            ihdr.WriteByte(0);
            ihdr.WriteByte(0);

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

        private static byte[] IconDuct(int size = 32)
        {
            Px[,] px = Blank(size);
            int m = 5;
            int w = size - 2 * m;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    bool edge = ((r == m || r == m + w) && m <= c && c <= m + w) ||
                                ((c == m || c == m + w) && m <= r && r <= m + w);
                    if (edge) px[r, c] = Teal;
                }
            int mid = size / 2;
            for (int c = m + 4; c < m + w - 3; c++)
                px[mid, c] = TealDark;
            return MakePng(size, size, px);
        }

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
            foreach (int c in new[] { 14, 15, 16 })
                px[10, c] = px[11, c] = White;
            for (int r = 14; r < 21; r++)
                px[r, 15] = White;
            return MakePng(size, size, px);
        }


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
    <MenuMacro UID=""WENTA_TAG"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_TAG_NM"">Tag Marker</Name>
        <Command>^C^C_.WENTATAG</Command>
        <HelpString xlate=""true"" UID=""WENTA_TAG_HS"">Place or edit a terminal, source or damper marker: WENTATAG</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_info_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_TRACE"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_TRACE_NM"">Trace Network</Name>
        <Command>^C^C_.WENTATRACE</Command>
        <HelpString xlate=""true"" UID=""WENTA_TRACE_HS"">Build the duct network from the selected centrelines and markers: WENTATRACE</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_duct_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_PRESSURE"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_PRESSURE_NM"">Pressure Drop</Name>
        <Command>^C^C_.WENTAPRESSURE</Command>
        <HelpString xlate=""true"" UID=""WENTA_PRESSURE_HS"">Critical-path pressure drop report with the WENTA-CRIT overlay: WENTAPRESSURE</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_SIZE"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_SIZE_NM"">Size Network</Name>
        <Command>^C^C_.WENTASIZE</Command>
        <HelpString xlate=""true"" UID=""WENTA_SIZE_HS"">Size every traced run and update the labels in place: WENTASIZE</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_duct_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_BALANCE"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_BALANCE_NM"">Balance Dampers</Name>
        <Command>^C^C_.WENTABALANCE</Command>
        <HelpString xlate=""true"" UID=""WENTA_BALANCE_HS"">Damper zeta and opening per terminal, written to the markers: WENTABALANCE</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_FAN"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_FAN_NM"">Select Fan</Name>
        <Command>^C^C_.WENTAFAN</Command>
        <HelpString xlate=""true"" UID=""WENTA_FAN_HS"">Pick a fan from a fan-curve catalog at the network duty point: WENTAFAN</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_INSULATE"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_INSULATE_NM"">Insulate</Name>
        <Command>^C^C_.WENTAINSULATE</Command>
        <HelpString xlate=""true"" UID=""WENTA_INSULATE_HS"">EN ISO 12241 insulation thickness for round runs, annotated on WENTA-INSUL: WENTAINSULATE</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_duct_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_SOUND"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_SOUND_NM"">Room Sound</Name>
        <Command>^C^C_.WENTASOUND</Command>
        <HelpString xlate=""true"" UID=""WENTA_SOUND_HS"">Regenerated noise per room against the NC limit: WENTASOUND</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_panel_32.png"" />
      </Macro>
    </MenuMacro>
    <MenuMacro UID=""WENTA_HELP"">
      <Macro type=""Any"">
        <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        <Name xlate=""true"" UID=""WENTA_HELP_NM"">Wenta Help</Name>
        <Command>^C^C_.WENTAHELP</Command>
        <HelpString xlate=""true"" UID=""WENTA_HELP_HS"">Command list and the EN/PL user guide: WENTAHELP</HelpString>
        <SmallImage Name="""" />
        <LargeImage Name=""wenta_info_32.png"" />
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
        <RibbonCommandButton UID=""WENTA_RBTN_TAG"" Id=""ZwRibbonCommandButton"" Text=""Tag Marker"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_TAG"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_TAG_TT"">Tag Marker</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_TRACE"" Id=""ZwRibbonCommandButton"" Text=""Trace Network"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_TRACE"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_TRACE_TT"">Trace Network</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_PRESSURE"" Id=""ZwRibbonCommandButton"" Text=""Pressure Drop"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_PRESSURE"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_PRESSURE_TT"">Pressure Drop</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_SIZE"" Id=""ZwRibbonCommandButton"" Text=""Size Network"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_SIZE"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_SIZE_TT"">Size Network</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_BALANCE"" Id=""ZwRibbonCommandButton"" Text=""Balance Dampers"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_BALANCE"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_BALANCE_TT"">Balance Dampers</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_FAN"" Id=""ZwRibbonCommandButton"" Text=""Select Fan"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_FAN"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_FAN_TT"">Select Fan</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_INSULATE"" Id=""ZwRibbonCommandButton"" Text=""Insulate"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_INSULATE"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_INSULATE_TT"">Insulate</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_SOUND"" Id=""ZwRibbonCommandButton"" Text=""Room Sound"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_SOUND"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_SOUND_TT"">Room Sound</TooltipTitle>
          <ModifiedRev MajorVersion=""0"" MinorVersion=""0"" UserVersion=""0"" />
        </RibbonCommandButton>
        <RibbonCommandButton UID=""WENTA_RBTN_HELP"" Id=""ZwRibbonCommandButton"" Text=""Wenta Help"" ButtonStyle=""LargeWithText"" MenuMacroID=""WENTA_HELP"" KeyTip="""">
          <TooltipTitle xlate=""true"" UID=""WENTA_RBTN_HELP_TT"">Wenta Help</TooltipTitle>
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


        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

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
                byte[] nameBytes = Utf8NoBom.GetBytes(name);
                uint crc = Crc32(data);
                uint size = (uint)data.Length;
                uint offset = (uint)_out.Position;

                DateTime t = DateTime.Now;
                int dosTime = (t.Hour << 11) | (t.Minute << 5) | (t.Second / 2);
                int dosDate = ((t.Year - 1980) << 9) | (t.Month << 5) | t.Day;

                LE32(_out, 0x04034B50);
                LE16(_out, 20);
                LE16(_out, 0);
                LE16(_out, 0);
                LE16(_out, dosTime);
                LE16(_out, dosDate);
                LE32(_out, crc);
                LE32(_out, size);
                LE32(_out, size);
                LE16(_out, nameBytes.Length);
                LE16(_out, 0);
                _out.Write(nameBytes, 0, nameBytes.Length);
                _out.Write(data, 0, data.Length);

                var cd = new MemoryStream();
                LE32(cd, 0x02014B50);
                LE16(cd, 20);
                LE16(cd, 20);
                LE16(cd, 0);
                LE16(cd, 0);
                LE16(cd, dosTime);
                LE16(cd, dosDate);
                LE32(cd, crc);
                LE32(cd, size);
                LE32(cd, size);
                LE16(cd, nameBytes.Length);
                LE16(cd, 0);
                LE16(cd, 0);
                LE16(cd, 0);
                LE16(cd, 0);
                LE32(cd, 0x01800000u);
                LE32(cd, offset);
                cd.Write(nameBytes, 0, nameBytes.Length);
                _central.Add(cd.ToArray());
            }

            public void Dispose()
            {
                uint cdStart = (uint)_out.Position;
                foreach (byte[] rec in _central)
                    _out.Write(rec, 0, rec.Length);
                uint cdSize = (uint)_out.Position - cdStart;

                LE32(_out, 0x06054B50);
                LE16(_out, 0);
                LE16(_out, 0);
                LE16(_out, _central.Count);
                LE16(_out, _central.Count);
                LE32(_out, cdSize);
                LE32(_out, cdStart);
                LE16(_out, 0);
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
