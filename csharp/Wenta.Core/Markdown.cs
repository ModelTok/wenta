using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wenta
{
    // Markdown -> standalone HTML for the user guides that WENTAHELP opens
    // (#37). It covers the subset docs/user-guide.*.md use: ATX headings,
    // paragraphs, "-" / "1." lists with indented continuation lines, fenced
    // code, pipe tables, "---" rules, code spans, **bold**, *italic*,
    // [links](url) and <https://autolinks>. Everything else is passed
    // through as escaped text, so the output never carries raw HTML from the
    // source. Self-contained (no other Wenta.Core file is needed), so
    // zwcad-plugin/tools/MakeHelp.cs compiles it on its own.
    public static class Markdown
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Rule = new Regex(@"^\s{0,3}(-\s*){3,}$");
        private static readonly Regex Item = new Regex(@"^( {0,3})([-*+]|\d+[.)])\s+(.*)$");
        private static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");
        private static readonly Regex Fence = new Regex(@"^\s{0,3}(```+|~~~+)\s*([\w+#.-]*)\s*$");

        public sealed class Section
        {
            public int Level;
            public string Id;
            public string Html;
        }

        // A full HTML document: <title> from the first level-1 heading (or
        // the fallback), a contents list of the level-2 and level-3
        // headings, then the body.
        public static string ToHtmlDocument(string markdown, string fallbackTitle, string lang)
        {
            List<Section> headings;
            string body = ToHtml(markdown, out headings);
            string title = fallbackTitle ?? "";
            foreach (Section h in headings)
                if (h.Level == 1) { title = StripTags(h.Html); break; }

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(Escape(lang ?? "en")).Append("\">\n<head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<meta name=\"generator\" content=\"Wenta.Markdown\">\n");
            sb.Append("<title>").Append(Escape(title)).Append("</title>\n");
            sb.Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");
            var toc = new StringBuilder();
            foreach (Section h in headings)
            {
                if (h.Level != 2 && h.Level != 3) continue;
                toc.Append("<li class=\"toc").Append(h.Level).Append("\"><a href=\"#").Append(h.Id).Append("\">")
                    .Append(StripLinks(h.Html)).Append("</a></li>\n");
            }
            // The contents list goes after the title (the first level-1
            // heading), or first when there is none.
            string nav = toc.Length > 0 ? "<nav class=\"toc\">\n<ul>\n" + toc + "</ul>\n</nav>\n" : "";
            int h1 = body.IndexOf("</h1>\n", StringComparison.Ordinal);
            int at = h1 >= 0 && body.StartsWith("<h1", StringComparison.Ordinal) ? h1 + 6 : 0;
            sb.Append(body, 0, at).Append(nav).Append(body, at, body.Length - at);
            sb.Append("</main>\n</body>\n</html>\n");
            return sb.ToString();
        }

        public static string ToHtml(string markdown)
        {
            List<Section> headings;
            return ToHtml(markdown, out headings);
        }

        public static string ToHtml(string markdown, out List<Section> headings)
        {
            headings = new List<Section>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            var para = new List<string>();
            string listTag = null;          // "ul" / "ol" while a list is open
            List<string> item = null;       // lines of the open list item

            Action flushPara = delegate
            {
                if (para.Count == 0) return;
                sb.Append("<p>").Append(Inline(string.Join("\n", para.ToArray()))).Append("</p>\n");
                para.Clear();
            };
            Action flushItem = delegate
            {
                if (item == null) return;
                sb.Append("<li>").Append(Inline(string.Join("\n", item.ToArray()))).Append("</li>\n");
                item = null;
            };
            Action closeList = delegate
            {
                flushItem();
                if (listTag != null) sb.Append("</").Append(listTag).Append(">\n");
                listTag = null;
            };

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                Match m;

                if ((m = Fence.Match(line)).Success)
                {
                    flushPara(); closeList();
                    string marker = m.Groups[1].Value;
                    string langName = m.Groups[2].Value;
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
                    {
                        code.Append(lines[i]).Append('\n');
                        i++;
                    }
                    i++; // closing fence (or end of input)
                    sb.Append("<pre><code");
                    if (langName.Length > 0) sb.Append(" class=\"language-").Append(Escape(langName)).Append("\"");
                    sb.Append(">").Append(Escape(code.ToString())).Append("</code></pre>\n");
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    flushPara();
                    if (listTag != null)
                    {
                        // A blank line ends the list unless the next
                        // non-blank line is another item of the same kind.
                        int j = i + 1;
                        while (j < lines.Length && lines[j].Trim().Length == 0) j++;
                        Match n = j < lines.Length ? Item.Match(lines[j]) : Match.Empty;
                        if (!(n.Success && KindOf(n.Groups[2].Value) == listTag)) closeList();
                        else flushItem();
                    }
                    i++;
                    continue;
                }

                if ((m = Heading.Match(line)).Success && line.StartsWith("#", StringComparison.Ordinal))
                {
                    flushPara(); closeList();
                    int level = m.Groups[1].Value.Length;
                    string html = Inline(m.Groups[2].Value);
                    string id = UniqueId(Slug(m.Groups[2].Value), ids);
                    headings.Add(new Section { Level = level, Id = id, Html = html });
                    sb.Append("<h").Append(level).Append(" id=\"").Append(id).Append("\">").Append(html)
                        .Append("</h").Append(level).Append(">\n");
                    i++;
                    continue;
                }

                if (para.Count == 0 && item == null && Rule.IsMatch(line))
                {
                    closeList();
                    sb.Append("<hr>\n");
                    i++;
                    continue;
                }

                if (line.TrimStart().StartsWith("|", StringComparison.Ordinal) && i + 1 < lines.Length
                    && TableSep.IsMatch(lines[i + 1]) && lines[i + 1].Contains("-"))
                {
                    flushPara(); closeList();
                    List<string> head = Cells(line);
                    List<string> align = Alignments(lines[i + 1]);
                    sb.Append("<table>\n<thead>\n<tr>");
                    for (int c = 0; c < head.Count; c++)
                        sb.Append("<th").Append(AlignAttr(align, c)).Append(">").Append(Inline(head[c])).Append("</th>");
                    sb.Append("</tr>\n</thead>\n<tbody>\n");
                    i += 2;
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal))
                    {
                        List<string> row = Cells(lines[i]);
                        sb.Append("<tr>");
                        for (int c = 0; c < head.Count; c++)
                            sb.Append("<td").Append(AlignAttr(align, c)).Append(">")
                                .Append(c < row.Count ? Inline(row[c]) : "").Append("</td>");
                        sb.Append("</tr>\n");
                        i++;
                    }
                    sb.Append("</tbody>\n</table>\n");
                    continue;
                }

                if ((m = Item.Match(line)).Success && (para.Count == 0 || listTag != null || CanInterrupt(m.Groups[2].Value)))
                {
                    flushPara();
                    string kind = KindOf(m.Groups[2].Value);
                    if (listTag != kind)
                    {
                        closeList();
                        listTag = kind;
                        sb.Append("<").Append(kind);
                        if (kind == "ol")
                        {
                            int start = int.Parse(m.Groups[2].Value.TrimEnd('.', ')'), CultureInfo.InvariantCulture);
                            if (start != 1) sb.Append(" start=\"").Append(start.ToString(CultureInfo.InvariantCulture)).Append("\"");
                        }
                        sb.Append(">\n");
                    }
                    flushItem();
                    item = new List<string> { m.Groups[3].Value };
                    i++;
                    continue;
                }

                if (item != null)
                {
                    // Continuation of the open list item (lazy or indented).
                    item.Add(line.Trim());
                    i++;
                    continue;
                }

                closeList();
                para.Add(line.Trim());
                i++;
            }
            flushPara();
            closeList();
            return sb.ToString();
        }

        // ---------------------------------------------------------------- inline

        private static readonly Regex Autolink = new Regex(@"&lt;((?:https?|mailto):[^\s&]+?)&gt;");
        private static readonly Regex Link = new Regex(@"\[([^\]\n]+)\]\(([^)\s]+)\)");
        private static readonly Regex Bold = new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Singleline);
        private static readonly Regex Italic = new Regex(@"(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])", RegexOptions.Singleline);

        public static string Inline(string text)
        {
            text = (text ?? "").Replace(Hold, ' ').Replace(Release, ' ');
            var sb = new StringBuilder();
            var spans = new List<string>();
            int pos = 0;
            while (pos < text.Length)
            {
                int open = text.IndexOf('`', pos);
                if (open < 0) { sb.Append(text.Substring(pos)); break; }
                int run = 1;
                while (open + run < text.Length && text[open + run] == '`') run++;
                string ticks = new string('`', run);
                int close = text.IndexOf(ticks, open + run, StringComparison.Ordinal);
                while (close >= 0 && close + run < text.Length && text[close + run] == '`')
                    close = text.IndexOf(ticks, close + run + 1, StringComparison.Ordinal);
                if (close < 0)
                {
                    sb.Append(text.Substring(pos, open + run - pos));
                    pos = open + run;
                    continue;
                }
                sb.Append(text.Substring(pos, open - pos));
                string code = text.Substring(open + run, close - open - run).Replace('\n', ' ');
                if (code.Length > 2 && code[0] == ' ' && code[code.Length - 1] == ' ') code = code.Substring(1, code.Length - 2);
                // Code spans are held back as placeholders so emphasis and
                // links may wrap them ("**`WENTAREAD`**").
                sb.Append(Hold).Append(spans.Count.ToString(CultureInfo.InvariantCulture)).Append(Release);
                spans.Add("<code>" + Escape(code) + "</code>");
                pos = close + run;
            }
            string html = Emphasis(sb.ToString());
            return spans.Count == 0 ? html : HeldSpan.Replace(html, delegate (Match m)
            {
                return spans[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)];
            });
        }

        private const char Hold = '\u0001', Release = '\u0002';
        private static readonly Regex HeldSpan = new Regex("\u0001(\\d+)\u0002");

        private static string Emphasis(string text)
        {
            string s = Escape(text);
            s = Autolink.Replace(s, "<a href=\"$1\">$1</a>");
            s = Link.Replace(s, delegate (Match m)
            {
                string href = m.Groups[2].Value;
                if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) href = "#";
                return "<a href=\"" + href + "\">" + m.Groups[1].Value + "</a>";
            });
            s = Bold.Replace(s, "<strong>$1</strong>");
            s = Italic.Replace(s, "<em>$1</em>");
            return s;
        }

        // ---------------------------------------------------------------- helpers

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // GitHub-style anchor: lower case, punctuation dropped, spaces to
        // hyphens; letters outside ASCII (Polish diacritics) are kept.
        public static string Slug(string heading)
        {
            var sb = new StringBuilder();
            foreach (char c in heading.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
            }
            return sb.Length == 0 ? "section" : sb.ToString();
        }

        private static string UniqueId(string id, HashSet<string> used)
        {
            string candidate = id;
            for (int n = 1; used.Contains(candidate); n++) candidate = id + "-" + n.ToString(CultureInfo.InvariantCulture);
            used.Add(candidate);
            return candidate;
        }

        // A bullet or an ordered list starting at 1 may interrupt a
        // paragraph (CommonMark); "2024." at a line start does not.
        private static bool CanInterrupt(string marker)
        {
            return !char.IsDigit(marker[0]) || marker == "1." || marker == "1)";
        }

        private static string KindOf(string marker)
        {
            return char.IsDigit(marker[0]) ? "ol" : "ul";
        }

        // Splits a table row on "|" outside code spans; "\|" is a literal bar.
        private static List<string> Cells(string row)
        {
            string s = row.Trim();
            if (s.StartsWith("|", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.EndsWith("|", StringComparison.Ordinal) && !s.EndsWith("\\|", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1);
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool inCode = false;
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '\\' && k + 1 < s.Length && s[k + 1] == '|') { cur.Append('|'); k++; continue; }
                if (c == '`') inCode = !inCode;
                if (c == '|' && !inCode) { cells.Add(cur.ToString().Trim()); cur.Length = 0; continue; }
                cur.Append(c);
            }
            cells.Add(cur.ToString().Trim());
            return cells;
        }

        private static List<string> Alignments(string sep)
        {
            var list = new List<string>();
            foreach (string cell in Cells(sep))
            {
                bool left = cell.StartsWith(":", StringComparison.Ordinal);
                bool right = cell.EndsWith(":", StringComparison.Ordinal);
                list.Add(left && right ? "center" : right ? "right" : left ? "left" : null);
            }
            return list;
        }

        private static string AlignAttr(List<string> align, int c)
        {
            return c < align.Count && align[c] != null ? " style=\"text-align:" + align[c] + "\"" : "";
        }

        private static string StripTags(string html)
        {
            string s = Regex.Replace(html, "<[^>]+>", "");
            return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
        }

        private static string StripLinks(string html)
        {
            return Regex.Replace(html, "</?a\\b[^>]*>", "");
        }

        private const string Css =
            ":root{--bg:#ffffff;--fg:#1f2328;--muted:#59636e;--line:#d1d9e0;--code:#f6f8fa;--link:#0969da}\n" +
            "@media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#9198a1;--line:#3d444d;--code:#151b23;--link:#4493f8}}\n" +
            "body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.55 'Segoe UI',system-ui,sans-serif}\n" +
            "main{max-width:960px;margin:0 auto;padding:24px 16px 64px}\n" +
            "h1,h2,h3,h4{line-height:1.25;margin:1.6em 0 .6em}h1{font-size:2em}h2{font-size:1.5em;border-bottom:1px solid var(--line);padding-bottom:.3em}h3{font-size:1.2em}\n" +
            "a{color:var(--link)}code{overflow-wrap:anywhere;font:0.9em Consolas,'Cascadia Mono',monospace;background:var(--code);padding:.1em .3em;border-radius:4px}\n" +
            "pre{background:var(--code);padding:12px 16px;border-radius:6px;overflow-x:auto}pre code{padding:0;background:none;overflow-wrap:normal}\n" +
            "table{border-collapse:collapse;margin:1em 0;display:block;overflow-x:auto}th,td{border:1px solid var(--line);padding:6px 12px;vertical-align:top}th{background:var(--code)}\n" +
            "hr{border:0;border-top:1px solid var(--line);margin:2em 0}\n" +
            "nav.toc{border:1px solid var(--line);border-radius:6px;padding:8px 16px;margin:16px 0}nav.toc ul{list-style:none;padding:0;margin:0}nav.toc li.toc3{padding-left:1.5em;font-size:.95em}\n";
    }
}
