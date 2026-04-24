using SpellWork.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SpellWork.Database
{
    // ======================================================================
    // WowheadSyncProgress
    // ======================================================================

    public sealed record WowheadSyncProgress(
        int    Done,
        int    Total,
        string Current,
        int    NodesSynced,
        int    SpellsSynced,
        bool   IsError,
        string? ErrorDetail);

    // ======================================================================
    // WowheadSyncService
    //
    // Iterates every class / spec / hero-talent combination from
    // TalentTreeControl.AllClasses, fetches each talent-calc page from
    // Wowhead via Playwright, then upserts the parsed nodes, connections,
    // and spells directly into the spellforge_tracker database, and
    // downloads any missing icon images to SQL\talentIcons\<class>\.
    //
    // One-time Playwright setup (run once after restoring NuGet packages):
    //   pwsh bin\Debug\net8.0-windows\playwright.ps1 install chromium
    // ======================================================================
    public static class WowheadSyncService
    {
        // Polite delay between requests so we do not hammer Wowhead.
        private const int DelayBetweenRequestsMs = 2_000;

        private static readonly HttpClient s_http = new();

        // ------------------------------------------------------------------ //
        // Public entry point
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Syncs every class/spec/hero combination.  Reports progress after each
        /// combination; errors are reported but do not abort the overall run.
        /// </summary>
        /// <param name="reimportSet">
        /// Set of (cls, spec, hero) combinations whose existing data should be deleted and
        /// re-imported.  Combinations that have data but are NOT in this set are skipped.
        /// Pass <c>null</c> to skip all existing combinations (import-missing-only mode).
        /// </param>
        public static async Task SyncAllAsync(
            IProgress<WowheadSyncProgress>? progress = null,
            CancellationToken ct = default,
            IReadOnlySet<(string cls, string spec, string hero)>? reimportSet = null)
        {
            var combinations = BuildCombinations();
            int total       = combinations.Count;
            int done        = 0;
            int totalNodes  = 0;
            int totalSpells = 0;

            foreach (var (cls, spec, hero) in combinations)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new WowheadSyncProgress(
                    done, total,
                    $"{cls} / {spec} / {hero}",
                    totalNodes, totalSpells,
                    false, null));

                try
                {
                    // Skip or delete existing data based on caller's per-combo selection.
                    if (SpellForgeTrackerDb.HasData(cls, spec, hero))
                    {
                        bool shouldReimport = reimportSet?.Contains((cls, spec, hero)) ?? false;
                        if (!shouldReimport)
                        {
                            progress?.Report(new WowheadSyncProgress(
                                done, total,
                                $"Skipped: {cls} / {spec} / {hero}",
                                totalNodes, totalSpells,
                                false, null));
                            done++;
                            continue;
                        }
                        SpellForgeTrackerDb.DeleteData(cls, spec, hero);
                    }

                    var result = await WowheadTalentParser.FetchAndParseAsync(cls, spec, hero);

                    // Trees[0]=class  Trees[1]=hero  Trees[2]=spec
                    int classCount = result.Trees.Length > 0 ? result.Trees[0].Nodes.Count : 0;
                    int heroCount  = result.Trees.Length > 1 ? result.Trees[1].Nodes.Count : 0;
                    int specCount  = result.Trees.Length > 2 ? result.Trees[2].Nodes.Count : 0;
                    int nodeCount  = classCount + heroCount + specCount;
                    int spellCount = result.Spells.Count;

                    // Warn explicitly when the hero tree parsed 0 nodes — this usually
                    // means the hero-tab click did not trigger rendering on Wowhead.
                    bool heroExpected = !string.IsNullOrEmpty(hero);
                    if (heroExpected && heroCount == 0)
                    {
                        progress?.Report(new WowheadSyncProgress(
                            done, total,
                            $"⚠ Hero tree empty for {cls} / {spec} / {hero}  " +
                            $"(class:{classCount} hero:0 spec:{specCount}) — hero tab click may have failed",
                            totalNodes, totalSpells,
                            true, "Hero tree rendered 0 nodes; re-import this spec to retry."));
                    }

                    // Build the spells enumerable with class/spec/hero context.
                    var spells = new List<(int, string, string, string, string, string)>(spellCount);
                    foreach (var s in result.Spells)
                        spells.Add((s.SpellId, s.SpellName, cls, spec, hero, s.TreeType));

                    SpellForgeTrackerDb.UpsertNodes(result.Trees, cls, spec, hero);
                    SpellForgeTrackerDb.UpsertConnections(result.Trees, cls, spec, hero);
                    SpellForgeTrackerDb.UpsertSpells(spells);

                    await DownloadIconsAsync(result.Trees, cls, ct);

                    totalNodes  += nodeCount;
                    totalSpells += spellCount;

                    // Success entry: show per-tree counts so hero:0 is immediately visible.
                    string heroTag = heroExpected ? $"  hero:{heroCount}" : string.Empty;
                    progress?.Report(new WowheadSyncProgress(
                        done, total,
                        $"{cls} / {spec} / {hero}  (class:{classCount}{heroTag} spec:{specCount})",
                        totalNodes, totalSpells,
                        false, null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Report the error but continue with the remaining combinations.
                    progress?.Report(new WowheadSyncProgress(
                        done, total,
                        $"{cls} / {spec} / {hero}",
                        totalNodes, totalSpells,
                        true, ex.Message));

                    // Brief back-off before the next request.
                    await Task.Delay(DelayBetweenRequestsMs, ct);
                }

                done++;

                if (done < total)
                    await Task.Delay(DelayBetweenRequestsMs, ct);
            }

            progress?.Report(new WowheadSyncProgress(
                total, total, "Done",
                totalNodes, totalSpells,
                false, null));
        }

        // ------------------------------------------------------------------ //
        // Icon downloading
        // ------------------------------------------------------------------ //

        private static async Task DownloadIconsAsync(
            IReadOnlyList<TalentTree> trees,
            string cls,
            CancellationToken ct)
        {
            string iconDir = FindOrCreateIconDir(cls);
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tree in trees)
                foreach (var node in tree.Nodes)
                {
                    if (!string.IsNullOrEmpty(node.IconName))    needed.Add(node.IconName);
                    if (!string.IsNullOrEmpty(node.AltIconName)) needed.Add(node.AltIconName);
                }

            foreach (var iconName in needed)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(iconDir, iconName);
                if (File.Exists(dest)) continue;

                var url = $"https://wow.zamimg.com/images/wow/icons/large/{iconName}";
                try
                {
                    var bytes = await s_http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(dest, bytes, ct);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[WowheadSyncService] Icon download failed {url}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Walks up from <see cref="AppContext.BaseDirectory"/> to find the SQL folder,
        /// then returns (and creates if needed) SQL\talentIcons\{cls}\.
        /// </summary>
        private static string FindOrCreateIconDir(string cls)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var sqlPath = Path.Combine(dir.FullName, "SQL");
                if (Directory.Exists(sqlPath))
                {
                    var iconDir = Path.Combine(sqlPath, "talentIcons", cls.ToLowerInvariant());
                    Directory.CreateDirectory(iconDir);
                    return iconDir;
                }
                dir = dir.Parent;
            }
            // Fallback: create next to the executable.
            var fallback = Path.Combine(AppContext.BaseDirectory, "SQL", "talentIcons", cls.ToLowerInvariant());
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        public static List<(string cls, string spec, string hero)> BuildCombinations()
        {
            var list = new List<(string, string, string)>();
            foreach (var cls in TalentTreeControl.AllClasses)
                foreach (var spec in cls.Specs)
                    foreach (var hero in spec.HeroTalents)
                        list.Add((cls.Name, spec.Name, hero));
            return list;
        }
    }
}
