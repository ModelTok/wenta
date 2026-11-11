using System;
using System.IO;
using System.Text;

// Build step (#37): docs/user-guide.<lang>.md -> user-guide.<lang>.html next
// to WentaZwcad.dll, for WENTAHELP. The conversion is Wenta.Markdown, which
// zwcad-plugin/build.cmd compiles into this tool; its tests run with the
// Wenta.Core suite.
//   MakeHelp.exe <input.md> <output.html> [lang]
internal static class MakeHelp
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: MakeHelp <input.md> <output.html> [lang]");
            return 2;
        }
        try
        {
            string md = File.ReadAllText(args[0], Encoding.UTF8);
            string lang = args.Length > 2 ? args[2] : "en";
            string html = Wenta.Markdown.ToHtmlDocument(md, Path.GetFileNameWithoutExtension(args[0]), lang);
            File.WriteAllText(args[1], html, new UTF8Encoding(false));
            Console.WriteLine("user guide written: " + args[1] + " (" + html.Length + " chars)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MakeHelp: " + ex.Message);
            return 1;
        }
    }
}
