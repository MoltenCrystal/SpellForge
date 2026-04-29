using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SpellWork.Spell;
using static SpellWork.Database.SpellForgeSniffsDb;

namespace SpellWork.Database
{
    // ======================================================================
    // SniffImporter
    //
    // Reads WowPacketParser _parsed.txt files line-by-line and inserts spell
    // events into the spellforge_sniffs database via SpellForgeSniffsDb.
    //
    // Handled opcodes (all others are silently skipped):
    //   SMSG_SPELL_GO                    -> sniff_spell_casts
    //   SMSG_AURA_UPDATE                 -> sniff_aura_events
    //   SMSG_SPELL_NON_MELEE_DAMAGE_LOG  -> sniff_damage_events
    //   SMSG_SPELL_HEAL_LOG              -> sniff_heal_events
    //   SMSG_SPELL_ENERGIZE_LOG          -> sniff_energize_events
    //   SMSG_SPELL_PERIODIC_AURA_LOG     -> sniff_periodic_events
    //   SMSG_SPELL_PREPARE + SMSG_SPELL_START  -> sniff_spell_substitutions
    //     (SPELL_PREPARE records the ServerCastID GUID; SPELL_START carries the
    //      actual SpellID.  GUID Entry fields are NOT used — they are opaque GUID
    //      metadata and do not reliably encode spell IDs.)
    // ======================================================================
    public sealed class SniffImporter
    {
        // ------------------------------------------------------------------ //
        // Events
        // ------------------------------------------------------------------ //

        /// <summary>Fired periodically during parsing. Args: packetsProcessed, opcodeName.</summary>
        public event Action<int, string>? OnProgress;

        /// <summary>Fired for each packet that fails to parse. Args: lineNumber, rawLine, exception (may be null).</summary>
        public event Action<int, string, Exception?>? OnParseError;

        // ------------------------------------------------------------------ //
        // Entry point
        // ------------------------------------------------------------------ //

        public SniffImportResult Import(string filePath, string? buildVersionOverride = null)
        {
            if (!File.Exists(filePath))
                return new SniffImportResult { Success = false, ErrorMessage = "File not found: " + filePath };

            if (SpellForgeSniffsDb.SessionExists(filePath))
                return new SniffImportResult { Success = false, ErrorMessage = "Already imported: " + filePath };

            string buildVersion = buildVersionOverride ?? ExtractBuild(filePath);

            // Insert session row with 0 packets; update count at the end
            int sessionId = SpellForgeSniffsDb.InsertSession(filePath, buildVersion, 0, null);

            // Accumulators — flushed every BatchSize rows
            var casts      = new List<SniffSpellCast>(BatchFlush);
            var auras      = new List<SniffAuraEvent>(BatchFlush);
            var damages    = new List<SniffDamageEvent>(BatchFlush);
            var heals      = new List<SniffHealEvent>(BatchFlush);
            var energizes  = new List<SniffEnergizeEvent>(BatchFlush);
            var periodics  = new List<SniffPeriodicEvent>(BatchFlush);

            // Spell substitutions from SMSG_SPELL_PREPARE + SMSG_SPELL_START pairs.
            // Key = client_spell_id (authoritative SpellID from SPELL_START).
            // Value = (serverCastSpell, visualId, castFlags, count).
            var substitutions = new Dictionary<int, (int serverCast, int visualId, int castFlags, int count)>();

            // Pending SPELL_PREPARE records: ServerCastGuid (hex) -> (provisional clientId, serverCastEntry)
            // clientId is overridden by the SpellID in the matching SMSG_SPELL_START.
            // serverCastEntry is the ServerCastID GUID Entry decoded by WPP (informational hint).
            var pendingPrepares = new Dictionary<string, (int clientId, int serverCastEntry)>(StringComparer.Ordinal);

            // Proc-chain inference needs casts in memory before flush — keep a sliding window
            // of the last few hundred casts (by session timestamp) to match triggered casts.
            var castWindow = new List<SniffSpellCast>(ProcWindowSize * 2);

            // Aura state: (unitGuid, spellId, slot) -> true means "currently active"
            var auraState = new Dictionary<(string, int, int), bool>();

            // Running totals for result
            int packetCount = 0, castsIns = 0, aurasIns = 0, dmgIns = 0,
                healIns = 0, enIns = 0, perIns = 0, subObs = 0;
            var affectedSpells = new HashSet<int>();

            // Epoch for relative timestamps
            DateTime? epoch = null;

            // Parser state
            string? currentOpcode  = null;
            int     currentPacket  = 0;
            int     currentLine    = 0;
            DateTime currentTime   = default;
            var     fieldLines     = new List<string>(64);

            void ProcessCurrentPacket()
            {
                if (currentOpcode == null) return;
                try
                {
                    long tsMs = epoch.HasValue
                        ? (long)(currentTime - epoch.Value).TotalMilliseconds
                        : 0;

                    switch (currentOpcode)
                    {
                        case "SMSG_SPELL_GO":
                        {
                            var cast = ParseSpellGo(sessionId, currentPacket, tsMs, fieldLines);
                            if (cast != null)
                            {
                                affectedSpells.Add(cast.SpellId);
                                // Proc chain: try to find a triggering cast in the window
                                cast = InferTrigger(cast, castWindow);
                                casts.Add(cast);
                                castWindow.Add(cast);
                                if (castWindow.Count > ProcWindowSize * 2)
                                    castWindow.RemoveAt(0);
                                castsIns++;
                            }
                            break;
                        }
                        case "SMSG_AURA_UPDATE":
                        {
                            var evts = ParseAuraUpdate(sessionId, currentPacket, tsMs, fieldLines, auraState);
                            foreach (var e in evts)
                            {
                                affectedSpells.Add(e.SpellId);
                                auras.Add(e);
                                aurasIns++;
                            }
                            break;
                        }
                        case "SMSG_SPELL_NON_MELEE_DAMAGE_LOG":
                        {
                            var dmg = ParseDamageLog(sessionId, currentPacket, tsMs, fieldLines);
                            if (dmg != null)
                            {
                                affectedSpells.Add(dmg.SpellId);
                                damages.Add(dmg);
                                dmgIns++;
                            }
                            break;
                        }
                        case "SMSG_SPELL_HEAL_LOG":
                        {
                            var heal = ParseHealLog(sessionId, currentPacket, tsMs, fieldLines);
                            if (heal != null)
                            {
                                affectedSpells.Add(heal.SpellId);
                                heals.Add(heal);
                                healIns++;
                            }
                            break;
                        }
                        case "SMSG_SPELL_ENERGIZE_LOG":
                        {
                            var en = ParseEnergizeLog(sessionId, currentPacket, tsMs, fieldLines);
                            if (en != null)
                            {
                                affectedSpells.Add(en.SpellId);
                                energizes.Add(en);
                                enIns++;
                            }
                            break;
                        }
                        case "SMSG_SPELL_PERIODIC_AURA_LOG":
                        {
                            var per = ParsePeriodicAuraLog(sessionId, currentPacket, tsMs, fieldLines);
                            if (per != null)
                            {
                                affectedSpells.Add(per.SpellId);
                                periodics.Add(per);
                                perIns++;
                            }
                            break;
                        }
                        case "SMSG_SPELL_PREPARE":
                        {
                            // Store ServerCastID GUID for SPELL_START matching, plus the
                            // GUID Entry hint for the server-side spell (informational).
                            var sub = ParseSpellPrepare(fieldLines);
                            if (sub.HasValue)
                                pendingPrepares[sub.Value.serverGuid] = (sub.Value.clientId, sub.Value.serverCastEntry);
                            break;
                        }
                        case "SMSG_SPELL_START":
                        {
                            // The only fields that matter: SpellID, SpellXSpellVisualID, CastFlags.
                            // If CastID matches a pending SPELL_PREPARE ServerCastGuid, record the
                            // full substitution entry using SpellID as the authoritative client spell.
                            string? startCastGuid = null;
                            int     startSpellId  = 0;
                            int     startVisualId = 0;
                            int     startCastFlags = 0;
                            foreach (string fLine in fieldLines)
                            {
                                string t = StripLinePrefixes(fLine.Trim());
                                if (t.StartsWith("CastID:", StringComparison.Ordinal))
                                {
                                    ParseGuid(GetValue(t), out _, out string sg);
                                    if (sg.Length > 0) startCastGuid = sg;
                                }
                                else if (TryGetTaggedInt(t, "SpellID",              out int sid))  startSpellId   = sid;
                                else if (TryGetTaggedInt(t, "SpellXSpellVisualID",  out int vis))  startVisualId  = vis;
                                else if (TryGetTaggedInt(t, "CastFlags",            out int cf))   startCastFlags = cf;
                            }
                            if (startCastGuid != null && startSpellId != 0
                                && pendingPrepares.TryGetValue(startCastGuid, out var pending))
                            {
                                pendingPrepares.Remove(startCastGuid);
                                if (substitutions.TryGetValue(startSpellId, out var existing))
                                {
                                    // Keep the most-observed visual/flags; increment count.
                                    substitutions[startSpellId] = (
                                        pending.serverCastEntry != 0 ? pending.serverCastEntry : existing.serverCast,
                                        startVisualId  != 0 ? startVisualId  : existing.visualId,
                                        startCastFlags != 0 ? startCastFlags : existing.castFlags,
                                        existing.count + 1);
                                }
                                else
                                {
                                    substitutions[startSpellId] = (pending.serverCastEntry, startVisualId, startCastFlags, 1);
                                }
                                subObs++;
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnParseError?.Invoke(currentLine, currentOpcode ?? string.Empty, ex);
                }

                // Flush batches
                FlushIf(casts,     BatchFlush, r => SpellForgeSniffsDb.BulkInsertCasts(sessionId, r));
                FlushIf(auras,     BatchFlush, r => SpellForgeSniffsDb.BulkInsertAuraEvents(sessionId, r));
                FlushIf(damages,   BatchFlush, r => SpellForgeSniffsDb.BulkInsertDamageEvents(sessionId, r));
                FlushIf(heals,     BatchFlush, r => SpellForgeSniffsDb.BulkInsertHealEvents(sessionId, r));
                FlushIf(energizes, BatchFlush, r => SpellForgeSniffsDb.BulkInsertEnergizeEvents(sessionId, r));
                FlushIf(periodics, BatchFlush, r => SpellForgeSniffsDb.BulkInsertPeriodicEvents(sessionId, r));
            }

            try
            {
                using var sr = new StreamReader(filePath, System.Text.Encoding.UTF8, true, 65536);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    currentLine++;
                    var m = PacketHeaderRegex.Match(line);
                    if (m.Success)
                    {
                        // Commit previous packet
                        ProcessCurrentPacket();

                        string opcodeName = m.Groups[1].Value;
                        if (DateTime.TryParseExact(m.Groups[2].Value,
                                "MM/dd/yyyy HH:mm:ss.fff",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None,
                                out DateTime ts))
                        {
                            currentTime = ts;
                            epoch ??= ts;
                        }

                        if (!int.TryParse(m.Groups[3].Value, out currentPacket))
                            currentPacket = 0;

                        currentOpcode = IsHandledOpcode(opcodeName) ? opcodeName : null;
                        fieldLines.Clear();
                        packetCount++;

                        if (packetCount % 5000 == 0)
                            OnProgress?.Invoke(packetCount, currentOpcode ?? string.Empty);
                    }
                    else if (currentOpcode != null)
                    {
                        // Field line belonging to the current handled packet
                        fieldLines.Add(line);
                    }
                }
                // Last packet
                ProcessCurrentPacket();
            }
            catch (Exception ex)
            {
                return new SniffImportResult
                {
                    Success = false, SessionId = sessionId,
                    ErrorMessage = ex.Message,
                };
            }

            // Final flush of any partial batches
            if (casts.Count     > 0) SpellForgeSniffsDb.BulkInsertCasts(sessionId, casts);
            if (auras.Count     > 0) SpellForgeSniffsDb.BulkInsertAuraEvents(sessionId, auras);
            if (damages.Count   > 0) SpellForgeSniffsDb.BulkInsertDamageEvents(sessionId, damages);
            if (heals.Count     > 0) SpellForgeSniffsDb.BulkInsertHealEvents(sessionId, heals);
            if (energizes.Count > 0) SpellForgeSniffsDb.BulkInsertEnergizeEvents(sessionId, energizes);
            if (periodics.Count > 0) SpellForgeSniffsDb.BulkInsertPeriodicEvents(sessionId, periodics);
            if (substitutions.Count > 0) SpellForgeSniffsDb.UpsertSpellSubstitutions(substitutions);

            SpellForgeSniffsDb.UpdateSessionPacketCount(sessionId, packetCount);
            SpellForgeSniffsDb.RefreshSummaryForSpells(affectedSpells);

            return new SniffImportResult
            {
                Success                  = true,
                SessionId                = sessionId,
                PacketsParsed            = packetCount,
                CastsInserted            = castsIns,
                AuraEventsInserted       = aurasIns,
                DamageEventsInserted     = dmgIns,
                HealEventsInserted       = healIns,
                EnergizeEventsInserted   = enIns,
                PeriodicEventsInserted   = perIns,
                SubstitutionObservations = subObs,
                AffectedSpellIds         = new List<int>(affectedSpells),
            };
        }

        // ------------------------------------------------------------------ //
        // Per-opcode parsers
        // ------------------------------------------------------------------ //

        // SMSG_SPELL_GO
        // V12 WPP output: every field line is prefixed with "(Cast) "
        // Hit targets use "(Cast) [N] HitTarget: Full: 0x..."
        // e.g. "(Cast) SpellID: 836", "(Cast) CasterUnit: Full: 0x..."
        private static SniffSpellCast? ParseSpellGo(
            int sessionId, int packetNum, long tsMs, List<string> lines)
        {
            int   spellId    = 0;
            int   castFlags  = 0;
            int   castTime   = 0;
            int   hitCount   = 0;
            int   missCount  = 0;
            string casterType = string.Empty, casterGuid = string.Empty;
            string targetType = string.Empty, targetGuid = string.Empty;
            bool  gotSpellId = false;
            // OriginalCastID is present when this cast was triggered by another spell.
            // Its Entry field encodes the parent spell ID directly in the GUID.
            string originalCastIdRaw = string.Empty;
            string castIdRaw = string.Empty;

            foreach (string line in lines)
            {
                // Strip any leading "(Word)" and/or "[N]" group-label prefixes that
                // WPP adds to distinguish fields (e.g. "(Cast) [0] HitTarget: ...")
                string t = StripLinePrefixes(line.Trim());

                if (TryGetTaggedInt(t, "SpellID", out int sid))
                { spellId = sid; gotSpellId = true; }
                else if (TryGetTaggedInt(t, "CastFlags", out int cf))
                { castFlags = cf; }
                else if (TryGetTaggedInt(t, "CastTime", out int ct))
                { castTime = ct; }
                // Capture the raw GUID strings for cast-chain resolution
                else if (t.StartsWith("CastID:", StringComparison.Ordinal))
                { castIdRaw = GetValue(t); }
                else if (t.StartsWith("OriginalCastID:", StringComparison.Ordinal))
                { originalCastIdRaw = GetValue(t); }
                // "HitTarget:" — note the colon so we don't match "HitTargetsCount:"
                else if (t.StartsWith("HitTarget:", StringComparison.Ordinal))
                {
                    hitCount++;
                    // First hit target becomes the "primary target"
                    if (hitCount == 1) ParseGuid(GetValue(t), out targetType, out targetGuid);
                }
                else if (t.StartsWith("MissTarget:", StringComparison.Ordinal))
                { missCount++; }
                // CasterUnit is the actual unit; CasterGUID may be the spell's invisible source
                else if (t.StartsWith("CasterUnit:", StringComparison.Ordinal))
                { ParseGuid(GetValue(t), out casterType, out casterGuid); }
            }

            if (!gotSpellId || spellId == 0) return null;

            // If OriginalCastID differs from CastID the server is telling us this cast was
            // triggered by another spell. Extract the parent spell from OriginalCastID's Entry.
            int? triggeredBySpellId = null;
            bool isTriggered = (castFlags & 0x80) != 0;
            if (originalCastIdRaw.Length > 0 && castIdRaw.Length > 0)
            {
                TryGetGuidEntry(originalCastIdRaw, out int origEntry);
                TryGetGuidEntry(castIdRaw,         out int castEntry);
                // origEntry != castEntry means the chain started from a different spell
                if (origEntry != 0 && origEntry != castEntry)
                {
                    triggeredBySpellId = origEntry;
                    isTriggered        = true;
                }
            }

            return new SniffSpellCast
            {
                SessionId          = sessionId,
                PacketNumber       = packetNum,
                TimestampMs        = tsMs,
                SpellId            = spellId,
                CasterType         = casterType,
                CasterGuid         = casterGuid,
                TargetType         = targetType,
                TargetGuid         = targetGuid,
                CastTimeMs         = castTime,
                CastFlags          = castFlags,
                IsTriggered        = isTriggered,
                HitCount           = hitCount,
                MissCount          = missCount,
                TriggeredBySpellId = triggeredBySpellId,
            };
        }

        // SMSG_AURA_UPDATE
        // V12 WPP format: "[N] FieldName: Value" (index at the START)
        // e.g. "[0] SpellID: 430191", "[0] HasAura: True", "[0] Slot: 0"
        // UnitGUID appears as a plain line AFTER all [N] indexed lines:
        //   "UnitGUID: Full: 0x..." (end of packet)
        private static List<SniffAuraEvent> ParseAuraUpdate(
            int sessionId, int packetNum, long tsMs, List<string> lines,
            Dictionary<(string, int, int), bool> auraState)
        {
            var result = new List<SniffAuraEvent>();

            // Build per-index dictionaries
            // key = outer aura index (0,1,2,...), value = field dict
            var byIndex = new Dictionary<int, Dictionary<string, string>>();

            // UnitGUID is written at the END of the packet (after all [N] lines)
            string unitType = string.Empty, unitGuid = string.Empty;

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Length == 0) continue;

                // Try indexed field first: "[N] FieldName: value"
                var im = IndexedFieldRegex.Match(t);
                if (im.Success)
                {
                    int    idx       = int.Parse(im.Groups[1].Value);
                    string fieldName = im.Groups[2].Value;
                    string value     = im.Groups[3].Value.Trim();

                    if (!byIndex.TryGetValue(idx, out var d))
                        byIndex[idx] = d = new Dictionary<string, string>(16, StringComparer.Ordinal);
                    d[fieldName] = value;
                    continue;
                }

                // Plain non-indexed line — "UnitGUID: Full: 0x..." appears here (end of packet)
                if (GuidLineRegex.IsMatch(t))
                    ParseGuid(GetValue(t), out unitType, out unitGuid);
            }

            foreach (var (idx, fields) in byIndex)
            {
                if (!fields.TryGetValue("HasAura", out string? hasAuraStr)) continue;
                bool hasAura = hasAuraStr.Equals("True", StringComparison.OrdinalIgnoreCase);

                int spellId = 0;
                if (fields.TryGetValue("SpellID", out string? sidStr))
                    TryParseIntField(sidStr, out spellId);   // strip " (SpellName)" annotation
                if (spellId == 0) continue;

                int slot = 0;
                if (fields.TryGetValue("Slot", out string? slotStr))
                    TryParseIntField(slotStr, out slot);

                int stacks = 1;
                if (fields.TryGetValue("Applications", out string? appStr) &&
                    TryParseIntField(appStr, out int appVal))
                    stacks = Math.Max(1, appVal); // 0 = "1 application" in WoW protocol

                int duration = 0, remaining = 0;
                if (fields.TryGetValue("Duration",  out string? durStr))  TryParseIntField(durStr,  out duration);
                if (fields.TryGetValue("Remaining", out string? remStr))  TryParseIntField(remStr,  out remaining);

                int auraFlags = 0;
                if (fields.TryGetValue("Flags", out string? flagStr))
                {
                    // V12 format: "11 (NoCaster, Positive, Scalable)" — plain decimal before space
                    // Old format: "0x0019"
                    string fs = flagStr.Trim();
                    int sp = fs.IndexOf(' ');
                    if (sp > 0) fs = fs.Substring(0, sp);
                    if (fs.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(fs.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int fv))
                            auraFlags = fv;
                    }
                    else int.TryParse(fs, out auraFlags);
                }

                string casterType2 = string.Empty, casterGuid2 = string.Empty;
                if (fields.TryGetValue("CastUnit", out string? cuStr))
                    ParseGuid(cuStr, out casterType2, out casterGuid2);

                // Determine event type
                var stateKey = (unitGuid, spellId, slot);
                AuraEventType evType;
                if (!hasAura)
                {
                    evType = AuraEventType.Removed;
                    auraState.Remove(stateKey);
                }
                else if (!auraState.ContainsKey(stateKey))
                {
                    evType = AuraEventType.Applied;
                    auraState[stateKey] = true;
                }
                else
                {
                    // Already known — refreshed or stack changed
                    evType = stacks > 1 ? AuraEventType.StackChanged : AuraEventType.Refreshed;
                }

                result.Add(new SniffAuraEvent
                {
                    SessionId    = sessionId,
                    PacketNumber = packetNum,
                    TimestampMs  = tsMs,
                    SpellId      = spellId,
                    UnitType     = unitType,
                    UnitGuid     = unitGuid,
                    CasterType   = casterType2,
                    CasterGuid   = casterGuid2,
                    EventType    = evType,
                    Slot         = slot,
                    StackCount   = stacks,
                    DurationMs   = duration,
                    RemainingMs  = remaining,
                    AuraFlags    = auraFlags,
                });
            }

            return result;
        }

        // SMSG_SPELL_NON_MELEE_DAMAGE_LOG
        // Me: target GUID, CasterGUID, SpellID, Damage, OverKill, SchoolMask,
        // Absorbed, Resisted, ShieldBlock, Periodic (bit), Flags (bits)
        private static SniffDamageEvent? ParseDamageLog(
            int sessionId, int packetNum, long tsMs, List<string> lines)
        {
            int    spellId = 0; bool gotSpellId = false;
            string casterType = string.Empty, casterGuid = string.Empty;
            string targetType = string.Empty, targetGuid = string.Empty;
            int    schoolMask = 0, damage = 0, overkill = 0;
            int    absorbed = 0, resisted = 0, blocked = 0;
            bool   isPeriodic = false, isCrit = false;

            foreach (string line in lines)
            {
                string t = line.Trim();
                // "Me: Player/0x..." = target
                if (t.StartsWith("Me:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out targetType, out targetGuid);
                else if (t.StartsWith("CasterGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out casterType, out casterGuid);
                else if (TryGetTaggedInt(t, "SpellID", out int sid))
                { spellId = sid; gotSpellId = true; }
                else if (TryGetTaggedInt(t, "Damage", out int d))       damage     = d;
                else if (TryGetTaggedInt(t, "OverKill", out int ok))    overkill   = Math.Max(0, ok);
                else if (TryGetTaggedInt(t, "SchoolMask", out int sm))  schoolMask = sm;
                else if (TryGetTaggedInt(t, "Absorbed", out int ab))    absorbed   = Math.Max(0, ab);
                else if (TryGetTaggedInt(t, "Resisted", out int re))    resisted   = Math.Max(0, re);
                else if (TryGetTaggedInt(t, "ShieldBlock", out int bl)) blocked    = Math.Max(0, bl);
                else if (t.StartsWith("Periodic:", StringComparison.Ordinal))
                    isPeriodic = GetValue(t).Equals("True", StringComparison.OrdinalIgnoreCase);
                // Critical is embedded in Flags bitmask — HitInfo flag HITINFO_CRITICALHIT = 0x02000000
                else if (t.StartsWith("Flags:", StringComparison.Ordinal))
                {
                    string fs = GetValue(t);
                    if (fs.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(fs.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int fv))
                        isCrit = (fv & 0x02000000) != 0;
                }
            }

            if (!gotSpellId || spellId == 0) return null;

            return new SniffDamageEvent
            {
                SessionId    = sessionId,
                PacketNumber = packetNum,
                TimestampMs  = tsMs,
                SpellId      = spellId,
                CasterType   = casterType, CasterGuid = casterGuid,
                TargetType   = targetType, TargetGuid = targetGuid,
                SchoolMask   = schoolMask,
                Damage       = damage,
                Overkill     = overkill,
                Absorbed     = absorbed,
                Resisted     = resisted,
                Blocked      = blocked,
                IsPeriodic   = isPeriodic,
                IsCritical   = isCrit,
            };
        }

        // SMSG_SPELL_HEAL_LOG
        // TargetGUID, CasterGUID, SpellID, Health (=heal), OriginalHeal, OverHeal, Absorbed, Crit
        private static SniffHealEvent? ParseHealLog(
            int sessionId, int packetNum, long tsMs, List<string> lines)
        {
            int    spellId = 0; bool gotSpellId = false;
            string casterType = string.Empty, casterGuid = string.Empty;
            string targetType = string.Empty, targetGuid = string.Empty;
            int    heal = 0, overheal = 0, absorbed = 0;
            bool   isCrit = false;

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("TargetGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out targetType, out targetGuid);
                else if (t.StartsWith("CasterGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out casterType, out casterGuid);
                else if (TryGetTaggedInt(t, "SpellID", out int sid))
                { spellId = sid; gotSpellId = true; }
                else if (TryGetTaggedInt(t, "Health", out int h))       heal     = h;
                else if (TryGetTaggedInt(t, "OverHeal", out int oh))    overheal = Math.Max(0, oh);
                else if (TryGetTaggedInt(t, "Absorbed", out int ab))    absorbed = Math.Max(0, ab);
                else if (t.StartsWith("Crit:", StringComparison.Ordinal))
                    isCrit = GetValue(t).Equals("True", StringComparison.OrdinalIgnoreCase);
            }

            if (!gotSpellId || spellId == 0) return null;

            return new SniffHealEvent
            {
                SessionId    = sessionId,
                PacketNumber = packetNum,
                TimestampMs  = tsMs,
                SpellId      = spellId,
                CasterType   = casterType, CasterGuid = casterGuid,
                TargetType   = targetType, TargetGuid = targetGuid,
                Heal         = heal,
                Overheal     = overheal,
                Absorbed     = absorbed,
                IsPeriodic   = false,
                IsCritical   = isCrit,
            };
        }

        // SMSG_SPELL_ENERGIZE_LOG
        // CasterGUID, TargetGUID, SpellID, Type, Amount, OverEnergize
        private static SniffEnergizeEvent? ParseEnergizeLog(
            int sessionId, int packetNum, long tsMs, List<string> lines)
        {
            int    spellId = 0; bool gotSpellId = false;
            string casterType = string.Empty, casterGuid = string.Empty;
            string targetType = string.Empty, targetGuid = string.Empty;
            int    powerType = 0, amount = 0, overEnergize = 0;

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("CasterGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out casterType, out casterGuid);
                else if (t.StartsWith("TargetGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out targetType, out targetGuid);
                else if (TryGetTaggedInt(t, "SpellID", out int sid))
                { spellId = sid; gotSpellId = true; }
                else if (TryGetTaggedInt(t, "Type", out int pt))          powerType    = pt;
                else if (TryGetTaggedInt(t, "Amount", out int a))         amount       = a;
                else if (TryGetTaggedInt(t, "OverEnergize", out int oe))  overEnergize = Math.Max(0, oe);
            }

            if (!gotSpellId || spellId == 0) return null;

            return new SniffEnergizeEvent
            {
                SessionId    = sessionId,
                PacketNumber = packetNum,
                TimestampMs  = tsMs,
                SpellId      = spellId,
                CasterType   = casterType, CasterGuid = casterGuid,
                TargetType   = targetType, TargetGuid = targetGuid,
                PowerType    = powerType,
                Amount       = amount,
                OverEnergize = overEnergize,
            };
        }

        // SMSG_SPELL_PERIODIC_AURA_LOG
        // TargetGUID, CasterGUID, SpellID        // PeriodicAuraLogEffectData [0]: { Effect, Amount, OriginalDamage, OverHealOrKill,
        //   SchoolMaskOrPower, AbsorbedOrAmplitude, Resisted, Crit }
        private static SniffPeriodicEvent? ParsePeriodicAuraLog(
            int sessionId, int packetNum, long tsMs, List<string> lines)
        {
            int    spellId = 0; bool gotSpellId = false;
            string casterType = string.Empty, casterGuid = string.Empty;
            string targetType = string.Empty, targetGuid = string.Empty;
            int    auraType = 0, schoolMask = 0, amount = 0, overAmount = 0, absorbed = 0;
            bool   isCrit = false;

            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("TargetGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out targetType, out targetGuid);
                else if (t.StartsWith("CasterGUID:", StringComparison.Ordinal))
                    ParseGuid(GetValue(t), out casterType, out casterGuid);
                else if (TryGetTaggedInt(t, "SpellID", out int sid))
                { spellId = sid; gotSpellId = true; }
                // PeriodicAuraLogEffectData fields (not index-bracketed in old WPP, but are in new)
                else if (TryGetTaggedInt(t, "Effect", out int ef))               auraType   = ef;
                else if (TryGetTaggedInt(t, "Amount", out int a))                amount     = a;
                else if (TryGetTaggedInt(t, "OverHealOrKill", out int oh))       overAmount = Math.Max(0, oh);
                else if (TryGetTaggedInt(t, "SchoolMaskOrPower", out int sm))    schoolMask = sm;
                else if (TryGetTaggedInt(t, "AbsorbedOrAmplitude", out int ab))  absorbed   = Math.Max(0, ab);
                else if (t.StartsWith("Crit:", StringComparison.Ordinal))
                    isCrit = GetValue(t).Equals("True", StringComparison.OrdinalIgnoreCase);
            }

            if (!gotSpellId || spellId == 0) return null;

            return new SniffPeriodicEvent
            {
                SessionId    = sessionId,
                PacketNumber = packetNum,
                TimestampMs  = tsMs,
                SpellId      = spellId,
                CasterType   = casterType, CasterGuid = casterGuid,
                TargetType   = targetType, TargetGuid = targetGuid,
                AuraType     = auraType,
                SchoolMask   = schoolMask,
                Amount       = amount,
                OverAmount   = overAmount,
                Absorbed     = absorbed,
                IsCritical   = isCrit,
            };
        }

        // SMSG_SPELL_PREPARE
        // Returns:
        //   clientId        — ClientCastID GUID Entry (provisional talent spell ID).
        //   serverCastEntry — ServerCastID GUID Entry: the underlying spell hint (e.g. 116).
        //                     "It's just a GUID — you could skip the spell ID in it and nothing
        //                     would change" — informational only; SpellID from SPELL_START wins.
        //   serverGuid      — Raw ServerCastID hex GUID for matching against SMSG_SPELL_START CastID.
        private static (int clientId, int serverCastEntry, string serverGuid)? ParseSpellPrepare(List<string> lines)
        {
            int     clientId        = 0;
            int     serverCastEntry = 0;
            string? serverGuid      = null;
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("ClientCastID:", StringComparison.Ordinal))
                    TryGetGuidEntry(GetValue(t), out clientId);
                else if (t.StartsWith("ServerCastID:", StringComparison.Ordinal))
                {
                    string raw = GetValue(t);
                    TryGetGuidEntry(raw, out serverCastEntry);
                    ParseGuid(raw, out _, out string sg);
                    if (sg.Length > 0) serverGuid = sg;
                }
            }
            if (serverGuid == null) return null;
            return (clientId, serverCastEntry, serverGuid);
        }

        // ------------------------------------------------------------------ //
        // Proc-chain inference
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Reverse map built from DBC: triggeredSpellId -> set of parent spells that
        /// cast it via SPELL_EFFECT_TRIGGER_SPELL / TRIGGER_SPELL_WITH_VALUE / TRIGGER_SPELL_2.
        /// Built lazily on first use so DBC is guaranteed to be loaded.
        /// </summary>
        private static Dictionary<int, HashSet<int>>? s_dbcTriggerMap;
        private static readonly object s_dbcTriggerLock = new();

        private static Dictionary<int, HashSet<int>> GetDbcTriggerMap()
        {
            if (s_dbcTriggerMap != null) return s_dbcTriggerMap;
            lock (s_dbcTriggerLock)
            {
                if (s_dbcTriggerMap != null) return s_dbcTriggerMap;
                var map = new Dictionary<int, HashSet<int>>();
                foreach (var (parentId, spell) in DBC.DBC.SpellInfoStore)
                {
                    foreach (var eff in spell.SpellEffectInfoStore)
                    {
                        int effType = (int)eff.Effect;
                        if (effType != (int)SpellEffects.SPELL_EFFECT_TRIGGER_SPELL &&
                            effType != (int)SpellEffects.SPELL_EFFECT_TRIGGER_SPELL_WITH_VALUE &&
                            effType != (int)SpellEffects.SPELL_EFFECT_TRIGGER_SPELL_2)
                            continue;
                        int triggeredId = eff.EffectTriggerSpell;
                        if (triggeredId == 0) continue;
                        if (!map.TryGetValue(triggeredId, out var parents))
                            map[triggeredId] = parents = new HashSet<int>();
                        parents.Add(parentId);
                    }
                }
                s_dbcTriggerMap = map;
                return map;
            }
        }

        private static SniffSpellCast InferTrigger(SniffSpellCast cast, List<SniffSpellCast> window)
        {
            // OriginalCastID in the packet already gave us the authoritative parent — no guessing needed.
            if (cast.TriggeredBySpellId != null) return cast;

            if (!cast.IsTriggered) return cast;

            // First pass: DBC-authoritative match.
            // If DBC says spell X triggers spell Y, and X was recently cast by the
            // same caster, that's a definitive link — prefer it over time-window guessing.
            var triggerMap = GetDbcTriggerMap();
            if (triggerMap.TryGetValue(cast.SpellId, out var knownParents))
            {
                long windowStart = cast.TimestampMs - ProcWindowMs;
                for (int i = window.Count - 1; i >= 0; i--)
                {
                    var c = window[i];
                    if (c.TimestampMs < windowStart) break;
                    if (!string.Equals(c.CasterGuid, cast.CasterGuid, StringComparison.Ordinal)) continue;
                    if (!knownParents.Contains(c.SpellId)) continue;
                    return WithTrigger(cast, c.SpellId);
                }
            }

            // Second pass: time-window fallback — nearest non-triggered cast from same caster.
            {
                long windowStart = cast.TimestampMs - ProcWindowMs;
                for (int i = window.Count - 1; i >= 0; i--)
                {
                    var c = window[i];
                    if (c.TimestampMs < windowStart) break;
                    if (c.IsTriggered) continue;
                    if (!string.Equals(c.CasterGuid, cast.CasterGuid, StringComparison.Ordinal)) continue;
                    return WithTrigger(cast, c.SpellId);
                }
            }

            return cast;
        }

        private static SniffSpellCast WithTrigger(SniffSpellCast cast, int triggeredBySpellId) =>
            new SniffSpellCast
            {
                SessionId          = cast.SessionId,
                PacketNumber       = cast.PacketNumber,
                TimestampMs        = cast.TimestampMs,
                SpellId            = cast.SpellId,
                CasterType         = cast.CasterType,
                CasterGuid         = cast.CasterGuid,
                TargetType         = cast.TargetType,
                TargetGuid         = cast.TargetGuid,
                CastTimeMs         = cast.CastTimeMs,
                CastFlags          = cast.CastFlags,
                IsTriggered        = true,
                HitCount           = cast.HitCount,
                MissCount          = cast.MissCount,
                TriggeredBySpellId = triggeredBySpellId,
            };

        // ------------------------------------------------------------------ //
        // Utility
        // ------------------------------------------------------------------ //

        // WPP packet header (V8+):
        // "ServerToClient: SMSG_SPELL_GO (0x9999) Length: 87 ConnIdx: 0 Time: 04/09/2026 16:37:01.234 Number: 123"
        private static readonly Regex PacketHeaderRegex = new(
            @"^(?:ServerToClient|ClientToServer):\s+(\w+)\s+\(0x[0-9A-Fa-f]+\).*Time:\s+(\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2}\.\d{3}).*Number:\s+(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Indexed field V12: "[0] SpellID: 44614"  ->  groups: index, fieldName, value
        // Nested lines like "[0] [1] Points: 10" are intentionally skipped because
        // group(2) would be "[1]" which does not match [\w.]+.
        private static readonly Regex IndexedFieldRegex = new(
            @"^\[(\d+)\]\s+([\w.]+):\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Plain GUID line — matches both formats:
        //   V12:  "FieldName: Full: 0xHEX..."
        //   Old:  "FieldName: TypeName/0xHEX"
        private static readonly Regex GuidLineRegex = new(
            @"^[\w]+:\s+(?:Full:\s+0x[0-9A-Fa-f]+|\w+/0x[0-9A-Fa-f]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Build version from filename: "12.0.1.66838"
        private static readonly Regex BuildRegex = new(
            @"(\d+\.\d+\.\d+\.\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Strips any number of leading "(Word)" or "[N]" group labels that WPP
        /// prepends to field lines.  Examples:
        ///   "(Cast) SpellID: 836"          -> "SpellID: 836"
        ///   "(Cast) [0] HitTarget: ..."    -> "HitTarget: ..."
        /// </summary>
        private static string StripLinePrefixes(string t)
        {
            while (true)
            {
                if (t.Length > 0 && t[0] == '(')
                {
                    int close = t.IndexOf(')', 1);
                    if (close < 0) break;
                    t = t.Substring(close + 1).TrimStart();
                }
                else if (t.Length > 0 && t[0] == '[')
                {
                    int close = t.IndexOf(']', 1);
                    if (close < 0) break;
                    t = t.Substring(close + 1).TrimStart();
                }
                else break;
            }
            return t;
        }

        private static readonly HashSet<string> HandledOpcodes = new(StringComparer.Ordinal)
        {
            "SMSG_SPELL_GO",
            "SMSG_SPELL_START",           // paired with SMSG_SPELL_PREPARE for talent spell detection
            "SMSG_AURA_UPDATE",
            "SMSG_SPELL_NON_MELEE_DAMAGE_LOG",
            "SMSG_SPELL_HEAL_LOG",
            "SMSG_SPELL_ENERGIZE_LOG",
            "SMSG_SPELL_PERIODIC_AURA_LOG",
            "SMSG_SPELL_PREPARE",
        };

        private const int BatchFlush    = 500;
        private const int ProcWindowSize = 200;  // max casts kept in sliding window
        private const long ProcWindowMs  = 100;  // 100ms proc chain window

        private static bool IsHandledOpcode(string name) => HandledOpcodes.Contains(name);

        private static string ExtractBuild(string filePath)
        {
            string name = Path.GetFileName(filePath);
            var m = BuildRegex.Match(name);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        /// <summary>
        /// Parses a WPP GUID field value into a type name and hex GUID string.
        ///
        /// WPP V12 (WowGuid128) format:
        ///   "Full: 0x{High:X16}{Low:X16} {TypeName}/{SubType} R{realm}/S{server} Map: {map} [Entry: {entry}] Low: {low} [Name: {name}]"
        ///   -> type = TypeName  (first word after the hex block)
        ///   -> guid = "0x{High:X16}{Low:X16}"  (34 chars)
        ///
        /// Old WPP (WowGuid64) format:
        ///   "{TypeName}/0x{HEX}"
        ///   -> type = TypeName  (last word before the slash that precedes 0x)
        ///   -> guid = "0x{HEX}"
        /// </summary>
        private static void ParseGuid(string raw, out string type, out string guid)
        {
            raw = raw.Trim();

            // Find the "0x" marker — present in both formats
            int oxIdx = raw.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (oxIdx < 0)
            {
                type = string.Empty;
                guid = raw;
                return;
            }

            // Extract hex: from "0x" to first whitespace (or end of string)
            int hexEnd = raw.IndexOfAny(s_guidTrimChars, oxIdx + 2);
            guid = hexEnd > 0
                ? raw.Substring(oxIdx, hexEnd - oxIdx)
                : raw.Substring(oxIdx);

            if (hexEnd > 0)
            {
                // V12 path: type is the first word after the hex block, before '/' or space
                // e.g. "Full: 0x...32chars... Player/0 R0/S0 ..."
                int typeStart = hexEnd;
                while (typeStart < raw.Length && raw[typeStart] == ' ') typeStart++;
                int typeEnd = typeStart;
                while (typeEnd < raw.Length && raw[typeEnd] != ' ' && raw[typeEnd] != '/') typeEnd++;
                type = raw.Substring(typeStart, typeEnd - typeStart);
            }
            else
            {
                // Old format fallback: type is the last word before the '/' preceding "0x"
                // e.g. "Player/0x1234ABCD"
                int slash = oxIdx > 0 ? raw.LastIndexOf('/', oxIdx - 1) : -1;
                if (slash > 0)
                {
                    string left = raw.Substring(0, slash).Trim();
                    int space = left.LastIndexOf(' ');
                    type = space >= 0 ? left.Substring(space + 1) : left;
                }
                else
                {
                    type = string.Empty;
                }
            }
        }

        private static readonly char[] s_guidTrimChars = { ' ', '\t' };

        /// <summary>
        /// Extracts the "Entry: N" integer from a WPP GUID raw value string.
        /// e.g. "Full: 0x... Cast/15 R.../S... Map: 2552 Entry: 228354 (Flurry) Low: ..."
        ///   -> entry = 228354
        /// Returns false if the GUID has no Entry component.
        /// </summary>
        private static bool TryGetGuidEntry(string guidValue, out int entry)
        {
            entry = 0;
            int idx = guidValue.IndexOf("Entry: ", StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += 7; // skip "Entry: "
            int end = idx;
            while (end < guidValue.Length && char.IsDigit(guidValue[end])) end++;
            return end > idx && int.TryParse(guidValue.Substring(idx, end - idx), out entry);
        }

        /// <summary>Gets the value part after the first colon on a line.</summary>
        private static string GetValue(string line)
        {
            int colon = line.IndexOf(':');
            return colon >= 0 ? line.Substring(colon + 1).Trim() : string.Empty;
        }

        /// <summary>
        /// Returns true and parses the integer value for a line that starts with
        /// fieldName followed by optional whitespace and a colon.
        /// Handles plain decimal and "0x…" hex values.
        /// Does NOT match indexed fields (those contain "[N]").
        /// </summary>
        private static bool TryGetTaggedInt(string line, string fieldName, out int value)
        {
            value = 0;
            // Must start with the field name; after the name must come ':' or whitespace+'['  (skip indexed)
            if (!line.StartsWith(fieldName, StringComparison.Ordinal)) return false;
            if (line.Length <= fieldName.Length) return false;
            char next = line[fieldName.Length];
            if (next == '[') return false; // indexed field — handled elsewhere
            if (next != ':' && next != ' ') return false;

            string val = GetValue(line);
            if (val.Length == 0) return false;

            // Strip anything after whitespace or '(' — e.g. "0x00000041 (SPELL_CAST_FLAGS_...)"
            int ws = val.IndexOfAny(new[] { ' ', '(' });
            if (ws > 0) val = val.Substring(0, ws);

            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val.Substring(2),
                        System.Globalization.NumberStyles.HexNumber, null, out int hex))
                { value = hex; return true; }
                return false;
            }
            return int.TryParse(val, out value);
        }

        /// <summary>
        /// Parses an integer from a WPP field value that may carry a trailing annotation,
        /// e.g. "44614 (Ring of Frost)" or "0x00000041 (SPELL_CAST_FLAGS_...)".
        /// Strips everything from the first space or '(' onwards before parsing.
        /// </summary>
        private static bool TryParseIntField(string? raw, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            string s = raw.Trim();
            int cut = s.IndexOfAny(new[] { ' ', '(' });
            if (cut > 0) s = s.Substring(0, cut);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value);
            return int.TryParse(s, out value);
        }

        private static void FlushIf<T>(List<T> list, int threshold, Action<IEnumerable<T>> writer)
        {
            if (list.Count < threshold) return;
            writer(list);
            list.Clear();
        }
    }

    // ======================================================================
    // SniffImportResult
    // ======================================================================
    public sealed class SniffImportResult
    {
        public bool         Success                  { get; init; }
        public int          SessionId                { get; init; }
        public int          PacketsParsed            { get; init; }
        public int          CastsInserted            { get; init; }
        public int          AuraEventsInserted       { get; init; }
        public int          DamageEventsInserted     { get; init; }
        public int          HealEventsInserted       { get; init; }
        public int          EnergizeEventsInserted   { get; init; }
        public int          PeriodicEventsInserted   { get; init; }
        public int          SubstitutionObservations { get; init; }
        public string?      ErrorMessage             { get; init; }
        public List<int>    AffectedSpellIds         { get; init; } = new();
    }
}
