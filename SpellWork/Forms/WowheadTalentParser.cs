using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

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
        // URL helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Converts a display name such as "Death Knight", "San'layn", or "Elune's Chosen"
        /// into the lowercase hyphenated slug used in Wowhead talent-calc URLs.
        /// Rules: letters/digits kept (lowercased), spaces and existing hyphens become '-',
        /// apostrophes and all other characters are stripped.
        /// </summary>
        internal static string ToUrlSlug(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                        sb.Append('-');
                }
                // apostrophes and other punctuation are dropped
            }
            while (sb.Length > 0 && sb[sb.Length - 1] == '-')
                sb.Length--;
            return sb.ToString();
        }

        /// <summary>Returns the Wowhead talent-calc URL for a given class/spec/hero combination.</summary>
        public static string BuildUrl(string className, string specName, string heroName)
            => $"https://www.wowhead.com/talent-calc/{ToUrlSlug(className)}/{ToUrlSlug(specName)}/{ToUrlSlug(heroName)}";

        // ------------------------------------------------------------------ //
        // Live fetch via Playwright (headless Chromium)
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Launches a headless Chromium browser via Playwright, navigates to the Wowhead
        /// beta talent calculator for the given class/spec/hero combination, waits for the
        /// React talent tree to render, then extracts and parses the page HTML.
        ///
        /// <para>One-time setup: after restoring NuGet packages run<br/>
        /// <c>pwsh bin\Debug\net8.0-windows\playwright.ps1 install chromium</c><br/>
        /// to download the Chromium binary (~180 MB).</para>
        /// </summary>
        public static async Task<ParseResult> FetchAndParseAsync(
            string className, string specName, string heroName)
        {
            string url = BuildUrl(className, specName, heroName);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true });

            var page = await browser.NewPageAsync();

            // Mimic a regular browser so Wowhead does not block the request.
            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["User-Agent"]      =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/124.0.0.0 Safari/537.36",
            });

            // DOMContentLoaded fires as soon as the initial DOM is ready.
            // NetworkIdle would time out because Wowhead keeps background XHR active.
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout   = 30_000,
            });

            // Wait for the class/spec tree talent nodes to render.
            try
            {
                await page.WaitForSelectorAsync(
                    "a[data-cell]",
                    new PageWaitForSelectorOptions { Timeout = 30_000 });
            }
            catch (TimeoutException)
            {
                // Selector may have changed; fall through and let ParseFromHtml report
                // the empty result so the caller can decide whether to skip or retry.
            }

            bool expectHero = !string.IsNullOrEmpty(heroName);
            if (expectHero)
            {
                // The hero selection is encoded in the URL slug, so Wowhead's React app
                // should auto-render the correct hero tree without any click.
                // Wait up to 20 s for it to appear before falling back to a tab click.
                bool heroRendered = false;
                try
                {
                    await page.WaitForSelectorAsync(
                        "[data-tree-type='hero'] a[data-cell]",
                        new PageWaitForSelectorOptions { Timeout = 20_000 });
                    heroRendered = true;
                }
                catch (TimeoutException) { }

                // Only click when the URL-encoded auto-render didn't produce nodes.
                // Clicking an already-active tab triggers a React re-render that briefly
                // empties the hero tree, which would produce a 0-node parse result.
                if (!heroRendered)
                {
                    await TryClickHeroTabAsync(page, heroName);
                    try
                    {
                        await page.WaitForSelectorAsync(
                            "[data-tree-type='hero'] a[data-cell]",
                            new PageWaitForSelectorOptions { Timeout = 15_000 });
                    }
                    catch (TimeoutException) { }
                }
            }

            string html   = await page.ContentAsync();
            var    result = ParseFromHtml(html, className, specName, heroName);

            // If hero nodes are still empty after all the above, one last attempt.
            if (expectHero && result.Trees.Length > 1 && result.Trees[1].Nodes.Count == 0)
            {
                await TryClickHeroTabAsync(page, heroName);
                try
                {
                    await page.WaitForSelectorAsync(
                        "[data-tree-type='hero'] a[data-cell]",
                        new PageWaitForSelectorOptions { Timeout = 10_000 });
                }
                catch (TimeoutException) { }
                html   = await page.ContentAsync();
                result = ParseFromHtml(html, className, specName, heroName);
            }

            if (result.Trees.All(t => t.Nodes.Count == 0))
                throw new InvalidOperationException(
                    $"No talent nodes found for {className}/{specName}/{heroName}.\n" +
                    $"URL: {url}\n" +
                    "Wowhead may have changed its HTML structure or the URL slug is wrong.");

            return result;
        }

        /// <summary>
        /// Attempts to click the hero-talent selector tab on a Wowhead talent-calc page
        /// so that the hero tree React component renders its nodes into the DOM.
        ///
        /// Wowhead does not auto-render the hero tree on page load — a tab click is
        /// required.  We try two strategies in order:
        ///   1. Playwright GetByText with exact match (works for most tab labels).
        ///   2. JavaScript tree-walker that finds the first visible text node whose
        ///      trimmed content matches the hero name and clicks its parent element.
        /// Both strategies fail silently; the caller is responsible for checking
        /// whether hero nodes actually appeared after the click.
        /// </summary>
        private static async Task TryClickHeroTabAsync(IPage page, string heroName)
        {
            // Strategy 1: Playwright text locator — fast, works when button text
            // is an exact match of the hero name.
            try
            {
                var loc = page.GetByText(heroName, new() { Exact = true });
                if (await loc.CountAsync() > 0)
                {
                    await loc.First.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    return;
                }
            }
            catch { }

            // Strategy 2: JavaScript walk — searches every visible text node for the
            // hero name (case-insensitive) and clicks its closest interactive ancestor.
            try
            {
                await page.EvaluateAsync(
                    """
                    (heroName) => {
                        const lower = heroName.toLowerCase();
                        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
                        let node;
                        while ((node = walker.nextNode())) {
                            if (node.textContent.trim().toLowerCase() !== lower) continue;
                            let el = node.parentElement;
                            // Walk up to find a clickable container
                            for (let i = 0; i < 5 && el; i++, el = el.parentElement) {
                                const tag = el.tagName;
                                if (tag === 'A' || tag === 'BUTTON' || el.getAttribute('role') === 'button' ||
                                    el.getAttribute('role') === 'tab' || el.onclick != null) {
                                    if (el.offsetParent !== null) { el.click(); return; }
                                }
                            }
                            // If no interactive ancestor found, click the direct parent if visible
                            const p = node.parentElement;
                            if (p && p.offsetParent !== null) { p.click(); return; }
                        }
                    }
                    """,
                    heroName);
            }
            catch { }
        }

        // Legacy stubs kept for backward-compat with any callers that use the
        // bundle-based approach (FetchPageDataAsync / BuildResultFromPageData).
        // They now delegate to the per-URL approach.
        public static async Task<object?> FetchPageDataAsync(string url)
        {
            // Return a sentinel that BuildResultFromPageData can detect.
            return await Task.FromResult<object?>(null);
        }

        public static void DumpBundleStructure(object? pageData, string outputDir) { }

        public static ParseResult BuildResultFromPageData(
            object? pageData, string className, string specName, string heroName)
        {
            // Bundle-based approach not implemented; callers should use FetchAndParseAsync.
            throw new NotImplementedException(
                "Use WowheadSyncService.SyncAllAsync() or FetchAndParseAsync() directly.");
        }

        // ------------------------------------------------------------------ //
        // HTML parsing
        // ------------------------------------------------------------------ //

        // Locates each per-tree container by its data-tree-type attribute.
        private static readonly Regex s_treeSectionRegex = new(
            @"data-tree-type=""(class|spec|hero)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches a complete <a class="...-talent-tree-talent …">…</a> element.
        private static readonly Regex s_talentElementRegex = new(
            @"<a\s[^>]*(?:class=""[^""]*-talent-tree-talent[^""]*""|data-cell=""\d+"")[^>]*>.*?</a>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Attributes extracted from within a talent element.
        private static readonly Regex s_nodeAttrRegex = new(
            @"(?<name>data-row|data-column|data-cell|data-full|data-choice-href0|data-choice-href1|href)" +
            @"=""(?<value>[^""]*)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Finds a WoW icon filename (e.g. "spell_arcane_arcane01.jpg") from a
        // zamimg.com large-icon background-image URL, skipping the questionmark fallback.
        private static readonly Regex s_iconUrlRegex = new(
            @"wow\.zamimg\.com/images/wow/icons/large/(?!inv_misc_questionmark\.jpg)([a-zA-Z0-9_.+\-]+\.jpg)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Node display name from the div inside the <a> element.
        private static readonly Regex s_nodeNameRegex = new(
            @"dragonflight-talent-tree-talent-name""[^>]*>([^<]+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Max-rank denominator from a points div (which sits outside the <a> tag).
        // Group 1 = data-cell value, Group 2 = max-rank denominator.
        private static readonly Regex s_pointsDivRegex = new(
            @"dragonflight-talent-tree-talent-points[^>]*data-cell=""(\d+)""[^>]*>(?:\d+/)?(\d+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches a complete connection <div> opening tag (the outer div only;
        // inner -line/-arrow divs lack data-from-cell so they are filtered out below).
        private static readonly Regex s_connectionTagRegex = new(
            @"<div[^>]+class=""[^""]*dragonflight-talent-tree-connection[^""]*""[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Extracts data-from-cell / data-to-cell from within a matched opening tag.
        private static readonly Regex s_fromCellAttrRegex = new(
            @"\bdata-from-cell=""(\d+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex s_toCellAttrRegex = new(
            @"\bdata-to-cell=""(\d+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Spell ID from a wowhead spell URL.
        private static readonly Regex s_spellHrefRegex = new(
            @"/spell=(\d+)/",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses talent nodes from the fully-rendered HTML of a Wowhead talent-calculator page.
        ///
        /// <para>Nodes are bucketed by the <c>data-tree-type</c> attribute on their containing
        /// <c>&lt;div class="dragonflight-talent-trees-tree"&gt;</c>, <em>not</em> by the
        /// <c>data-talent-type</c> attribute (which describes the node kind: active/passive/choice).</para>
        ///
        /// <para>Result order: <c>Trees[0]</c> = class tree, <c>Trees[1]</c> = hero tree,
        /// <c>Trees[2]</c> = spec tree — matching <c>SpellForgeTrackerDb.s_treeTypes</c>.</para>
        /// </summary>
        public static ParseResult ParseFromHtml(
            string html,
            string className, string specName, string heroName)
        {
            var buckets = new Dictionary<string, List<TalentNode>>
            {
                ["class"] = new(),
                ["spec"]  = new(),
                ["hero"]  = new(),
            };
            var connectionsByTree = new Dictionary<string, Dictionary<int, List<int>>>
            {
                ["class"] = new(),
                ["spec"]  = new(),
                ["hero"]  = new(),
            };
            var spells = new List<(int, string, string)>();

            // Split HTML at each data-tree-type boundary so nodes go into the correct bucket.
            var sectionMatches = s_treeSectionRegex.Matches(html);
            for (int si = 0; si < sectionMatches.Count; si++)
            {
                string treeType = sectionMatches[si].Groups[1].Value.ToLowerInvariant();
                if (!buckets.ContainsKey(treeType)) continue;

                int sectionStart = sectionMatches[si].Index;
                int sectionEnd   = si + 1 < sectionMatches.Count
                                   ? sectionMatches[si + 1].Index
                                   : html.Length;
                string section = html[sectionStart..sectionEnd];

                // ── max-rank map (points divs are siblings of <a>, not children) ──
                var maxRankMap = new Dictionary<int, int>();
                foreach (Match pm in s_pointsDivRegex.Matches(section))
                {
                    if (int.TryParse(pm.Groups[1].Value, out int cid) &&
                        int.TryParse(pm.Groups[2].Value, out int mr) && mr > 0)
                        maxRankMap[cid] = mr;
                }

                // ── connections (two-step: find opening tag, then extract attrs) ──
                var connMap = connectionsByTree[treeType];
                foreach (Match cm in s_connectionTagRegex.Matches(section))
                {
                    string tag = cm.Value;
                    var fromM  = s_fromCellAttrRegex.Match(tag);
                    var toM    = s_toCellAttrRegex.Match(tag);
                    if (!fromM.Success || !toM.Success) continue;   // inner -line/-arrow divs
                    if (!int.TryParse(fromM.Groups[1].Value, out int from)) continue;
                    if (!int.TryParse(toM.Groups[1].Value,   out int to))   continue;
                    if (!connMap.TryGetValue(from, out var toList))
                        connMap[from] = toList = new List<int>();
                    if (!toList.Contains(to))
                        toList.Add(to);
                }

                // ── talent nodes ──
                foreach (Match em in s_talentElementRegex.Matches(section))
                {
                    string element = em.Value;

                    int    row = -1, col = -1, cellId = -1;
                    bool   isGate  = false;
                    int    spellId = 0, altSpellId = 0;

                    foreach (Match attr in s_nodeAttrRegex.Matches(element))
                    {
                        string an = attr.Groups["name"].Value.ToLowerInvariant();
                        string av = attr.Groups["value"].Value;
                        switch (an)
                        {
                            case "data-row":          int.TryParse(av, out row);       break;
                            case "data-column":       int.TryParse(av, out col);       break;
                            case "data-cell":         int.TryParse(av, out cellId);    break;
                            case "data-full":         isGate = av == "1";              break;
                            case "href":
                                var hm = s_spellHrefRegex.Match(av);
                                if (hm.Success) int.TryParse(hm.Groups[1].Value, out spellId);
                                break;
                            case "data-choice-href0":
                                var hm0 = s_spellHrefRegex.Match(av);
                                if (hm0.Success) int.TryParse(hm0.Groups[1].Value, out spellId);
                                break;
                            case "data-choice-href1":
                                var hm1 = s_spellHrefRegex.Match(av);
                                if (hm1.Success) int.TryParse(hm1.Groups[1].Value, out altSpellId);
                                break;
                        }
                    }

                    if (cellId < 0) continue;

                    // Icons: each dragonflight-talent-tree-talent-inner-background div has a
                    // real icon URL followed by a questionmark fallback. The regex skips the
                    // questionmark, so match 0 = icon for choice-option-0 (or only icon),
                    // match 1 = icon for choice-option-1 (choice nodes only).
                    var iconMatches = s_iconUrlRegex.Matches(element);
                    string iconName    = iconMatches.Count > 0 ? iconMatches[0].Groups[1].Value : string.Empty;
                    string altIconName = iconMatches.Count > 1 ? iconMatches[1].Groups[1].Value : string.Empty;

                    // Node name (may be "A / B" for choice nodes)
                    var nameMatch = s_nodeNameRegex.Match(element);
                    string spellName = nameMatch.Success
                        ? nameMatch.Groups[1].Value.Trim()
                        : $"Node {cellId}";

                    int maxRank = maxRankMap.TryGetValue(cellId, out var mr) ? mr : 1;

                    var node = new TalentNode
                    {
                        Id          = cellId,
                        Row         = row >= 0 ? row : cellId / 19,
                        Col         = col >= 0 ? col : cellId % 19,
                        MaxRank     = maxRank,
                        IsGate      = isGate,
                        SpellId     = spellId > 0 ? spellId : altSpellId,
                        AltSpellId  = altSpellId,
                        Name        = spellName,
                        IconName    = iconName,
                        AltIconName = altIconName,
                    };

                    if (!buckets[treeType].Exists(n => n.Id == cellId))
                        buckets[treeType].Add(node);

                    if (spellId > 0)    spells.Add((spellId,    spellName, treeType));
                    if (altSpellId > 0 && altSpellId != spellId)
                        spells.Add((altSpellId, spellName, treeType));
                }
            }

            // Populate ChildIds from the connection maps.
            foreach (var (tt, nodeList) in buckets)
            {
                var connMap = connectionsByTree[tt];
                foreach (var node in nodeList)
                {
                    if (connMap.TryGetValue(node.Id, out var children))
                        node.ChildIds.AddRange(children);
                }
            }

            // Order: [0] = class, [1] = hero, [2] = spec  (matches SpellForgeTrackerDb.s_treeTypes)
            return new ParseResult
            {
                Trees =
                [
                    new TalentTree { Name = className, Nodes = buckets["class"] },
                    new TalentTree { Name = heroName,  Nodes = buckets["hero"]  },
                    new TalentTree { Name = specName,  Nodes = buckets["spec"]  },
                ],
                Spells = spells,
            };
        }
    }
}
