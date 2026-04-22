using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpellWork.Database
{
    // ======================================================================
    // SniffPromptGenerator
    //
    // Produces a "=== SNIFF DATABASE EVIDENCE ===" section to be embedded
    // inside BuildImplementPrompt in TalentTreeControl.  The block contains
    // cast statistics, aura observations and damage metrics from the
    // spellforge_sniffs database — all real data captured from packet sniffs.
    //
    // Returns null when the sniffs DB is unreachable or has no data for the
    // requested spell, so callers can omit the section gracefully.
    // ======================================================================
    public static class SniffPromptGenerator
    {
        // ------------------------------------------------------------------
        // Public entry point
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the sniff-evidence section text for <paramref name="rootSpellId"/>
        /// and every spell in <paramref name="relatedSpells"/>.
        /// Returns <c>null</c> if the database is unreachable or no data exists.
        /// </summary>
        public static string? BuildEvidenceBlock(
            int rootSpellId,
            IReadOnlyList<Spell.SpellInfo> relatedSpells)
        {
            // Collect IDs to query
            var ids = new List<int> { rootSpellId };
            foreach (var r in relatedSpells)
                if (!ids.Contains(r.ID)) ids.Add(r.ID);

            // Check whether we have any data at all before building output
            bool anyData = false;
            foreach (int id in ids)
            {
                if (!TryGetSummary(id, out var chk)) continue;
                if (chk != null) { anyData = true; break; }
            }
            if (!anyData) return null;

            var sb = new StringBuilder();
            sb.AppendLine("=== SNIFF DATABASE EVIDENCE ===");
            sb.AppendLine("(Statistics aggregated from real packet captures — use to guide implementation)");

            foreach (int spellId in ids)
            {
                DBC.DBC.SpellInfoStore.TryGetValue(spellId, out var spell);
                string name = spell?.NameAndSubname ?? $"Spell {spellId}";

                sb.AppendLine();
                sb.AppendLine($"--- {name} (ID: {spellId}) ---");

                // ── Summary row ──────────────────────────────────────────
                if (TryGetSummary(spellId, out var sum) && sum != null)
                {
                    sb.AppendLine($"  Casts       : {sum.TotalCasts:N0} total" +
                                  $"  (triggered: {sum.TriggeredCount:N0}" +
                                  $"  instant: {sum.InstantCastCount:N0}" +
                                  $"  sessions: {sum.UniqueSessions})");

                    if (sum.MaxDamage > 0)
                        sb.AppendLine($"  Damage      : avg {sum.AvgDamage:N0}" +
                                      $"  min {sum.MinDamage:N0}" +
                                      $"  max {sum.MaxDamage:N0}");

                    if (sum.AvgHeal > 0)
                        sb.AppendLine($"  Heal        : avg {sum.AvgHeal:N0}");

                    if (sum.AvgEnergize > 0)
                        sb.AppendLine($"  Energize    : avg {sum.AvgEnergize:N0}");

                    if (sum.AuraAppliedCount > 0)
                        sb.AppendLine($"  Aura events : applied {sum.AuraAppliedCount:N0}" +
                                      $"  removed {sum.AuraRemovedCount:N0}");

                    sb.AppendLine($"  Build       : {sum.LastSeenBuild}");
                }
                else
                {
                    sb.AppendLine("  (no summary data for this spell)");
                    continue;
                }

                // ── Cast sample (trigger chain analysis) ─────────────────
                var casts = SpellForgeSniffsDb.GetCastsForSpell(spellId, 20);
                if (casts.Count > 0)
                {
                    bool anyTriggered = casts.Any(c => c.IsTriggered);
                    if (anyTriggered)
                    {
                        var trigFreq = casts
                            .Where(c => c.TriggeredBySpellId.HasValue)
                            .GroupBy(c => c.TriggeredBySpellId!.Value)
                            .OrderByDescending(g => g.Count())
                            .Take(5)
                            .ToList();

                        if (trigFreq.Count > 0)
                        {
                            sb.AppendLine("  Triggered by (top sources):");
                            foreach (var g in trigFreq)
                            {
                                var pName = DBC.DBC.SpellInfoStore.TryGetValue(g.Key, out var pSp)
                                    ? pSp.NameAndSubname : $"ID {g.Key}";
                                sb.AppendLine($"    {g.Key}: {pName}  ×{g.Count()}");
                            }
                        }
                    }

                    int avgHit  = (int)casts.Average(c => c.HitCount);
                    int avgMiss = (int)casts.Average(c => c.MissCount);
                    sb.AppendLine($"  Cast sample : N={casts.Count}" +
                                  $"  avgHits={avgHit}" +
                                  $"  avgMisses={avgMiss}");
                }

                // ── Aura durations and stack depth ────────────────────────
                var auras = SpellForgeSniffsDb.GetAuraEventsForSpell(spellId, 20);
                if (auras.Count > 0)
                {
                    var durSamples = auras
                        .Where(a => a.DurationMs > 0)
                        .Select(a => a.DurationMs)
                        .ToList();

                    if (durSamples.Count > 0)
                    {
                        int minDur = durSamples.Min();
                        int maxDur = durSamples.Max();
                        sb.AppendLine(minDur == maxDur
                            ? $"  Aura dur    : {minDur} ms"
                            : $"  Aura dur    : {minDur}–{maxDur} ms  (varies)");
                    }

                    int maxStacks = auras.Max(a => a.StackCount);
                    if (maxStacks > 1)
                        sb.AppendLine($"  Max stacks  : {maxStacks}");
                }

                // ── Crit rate and periodic flag from damage log ───────────
                var dmg = SpellForgeSniffsDb.GetDamageEventsForSpell(spellId, 100);
                if (dmg.Count > 0)
                {
                    double critRate = dmg.Count(d => d.IsCritical) * 100.0 / dmg.Count;
                    sb.AppendLine($"  Crit rate   : {critRate:F1}%  (N={dmg.Count})");
                    if (dmg.Any(d => d.IsPeriodic))
                        sb.AppendLine("  Periodic    : yes");
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Helper
        // ------------------------------------------------------------------

        private static bool TryGetSummary(int spellId, out SpellForgeSniffsDb.SniffSpellSummary? summary)
        {
            try
            {
                summary = SpellForgeSniffsDb.GetSummary(spellId);
                return true;
            }
            catch
            {
                summary = null;
                return false;
            }
        }
    }
}
