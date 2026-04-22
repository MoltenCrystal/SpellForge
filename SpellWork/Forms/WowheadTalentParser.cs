using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpellWork.Forms
{
    /// <summary>
    /// Parses Wowhead talent-calculator HTML pages into <see cref="TalentTree"/> objects.
    /// Save the Wowhead talent-calculator page from your browser (fully loaded) and pass
    /// the file content to <see cref="ParseFromHtml"/>.
    /// </summary>
    public static class WowheadTalentParser
    {
        // ------------------------------------------------------------------ //
        // Result type
        // ------------------------------------------------------------------ //

        public sealed class ParseResult
        {
            /// <summary>[0] = class tree, [1] = hero tree, [2] = spec tree.</summary>
            public TalentTree[] Trees { get; init; } = Array.Empty<TalentTree>();

            /// <summary>Every talent spell discovered during parse (spellId, spellName, treeType).</summary>
            public List<(int SpellId, string SpellName, string TreeType)> Spells { get; init; } = new();
        }

        // ------------------------------------------------------------------ //
        // Online fetching stubs (not yet implemented)
        // ------------------------------------------------------------------ //

        public static string BuildUrl(string className, string specName, string heroName)
            => $"https://www.wowhead.com/talent-calc/{className.ToLowerInvariant()}";

        public static Task<ParseResult> FetchAndParseAsync(string className, string specName, string heroName)
            => throw new NotImplementedException("Online fetching is not implemented. Use ParseFromHtml with a locally saved page instead.");

        public static Task<object?> FetchPageDataAsync(string url)
            => throw new NotImplementedException("Online fetching is not implemented.");

        public static void DumpBundleStructure(object? pageData, string outputDir) { }

        public static ParseResult BuildResultFromPageData(object? pageData, string className, string specName, string heroName)
            => throw new NotImplementedException("Online fetching is not implemented.");

        // ------------------------------------------------------------------ //
        // HTML parsing
        // ------------------------------------------------------------------ //

        // Matches a single opening <a …> tag that has the talent class.
        private static readonly Regex s_talentAnchorRegex = new(
            @"<a\s[^>]*class=""[^""]*dragonflight-talent-tree-talent[^""]*""[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex s_attrRegex = new(
            @"(?<name>data-row|data-column|data-cell|data-talent-type|aria-label|href)" +
            @"=""(?<value>[^""]*)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex s_spellHrefRegex = new(
            @"/spell=(?<id>\d+)/",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses talent nodes directly from the HTML of a saved Wowhead talent-calculator page.
        /// Reads every <c>&lt;a class="dragonflight-talent-tree-talent"&gt;</c> anchor and
        /// extracts <c>data-row</c>, <c>data-column</c>, <c>data-cell</c>,
        /// <c>data-talent-type</c>, the spell ID from the <c>href</c>, and the
        /// spell name from <c>aria-label</c>.
        ///
        /// <c>data-talent-type</c> mapping: 0 = class, 1 = spec, 2 = hero.
        /// </summary>
        public static ParseResult ParseFromHtml(
            string html,
            string className, string specName, string heroName)
        {
            // talent-type bucket index ? node list
            var buckets = new Dictionary<int, List<TalentNode>>
            {
                [0] = new(), // class
                [1] = new(), // spec
                [2] = new(), // hero
            };
            var spells = new List<(int, string, string)>();

            foreach (Match anchorMatch in s_talentAnchorRegex.Matches(html))
            {
                string tag = anchorMatch.Value;

                int    row        = -1, col = -1, cellId = -1, talentType = -1;
                int    spellId    = 0;
                string spellName  = string.Empty;

                foreach (Match attr in s_attrRegex.Matches(tag))
                {
                    string attrName  = attr.Groups["name"].Value.ToLowerInvariant();
                    string attrValue = attr.Groups["value"].Value;

                    switch (attrName)
                    {
                        case "data-row":         _ = int.TryParse(attrValue, out row);        break;
                        case "data-column":      _ = int.TryParse(attrValue, out col);        break;
                        case "data-cell":        _ = int.TryParse(attrValue, out cellId);     break;
                        case "data-talent-type": _ = int.TryParse(attrValue, out talentType); break;
                        case "aria-label":       spellName = attrValue;                       break;
                        case "href":
                            var hrefM = s_spellHrefRegex.Match(attrValue);
                            if (hrefM.Success) _ = int.TryParse(hrefM.Groups["id"].Value, out spellId);
                            break;
                    }
                }

                if (cellId < 0) continue;

                int nodeRow = row >= 0 ? row : cellId / 19;
                int nodeCol = col >= 0 ? col : cellId % 19;

                var node = new TalentNode
                {
                    Id      = cellId,
                    Row     = nodeRow,
                    Col     = nodeCol,
                    MaxRank = 1,
                    IsGate  = false,
                    SpellId = spellId,
                    Name    = string.IsNullOrEmpty(spellName) ? $"Node {cellId}" : spellName,
                };

                // Clamp unknown types to class bucket.
                int bucket = talentType is 0 or 1 or 2 ? talentType : 0;
                if (!buckets[bucket].Exists(n => n.Id == cellId))
                    buckets[bucket].Add(node);

                if (spellId > 0)
                {
                    string tt = bucket == 0 ? "class" : bucket == 1 ? "spec" : "hero";
                    spells.Add((spellId, node.Name, tt));
                }
            }

            // Order: [0]=class, [1]=hero, [2]=spec  (same as SpellForgeTrackerDb.s_treeTypes)
            return new ParseResult
            {
                Trees =
                [
                    new TalentTree { Name = className, Nodes = buckets[0] },
                    new TalentTree { Name = heroName,  Nodes = buckets[2] },
                    new TalentTree { Name = specName,  Nodes = buckets[1] },
                ],
                Spells = spells,
            };
        }
    }
}
