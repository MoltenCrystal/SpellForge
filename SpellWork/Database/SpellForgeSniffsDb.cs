using MySql.Data.MySqlClient;
using SpellWork.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpellWork.Database
{
    // ======================================================================
    // SpellForgeSniffsDb
    //
    // Read/write access to the *separate* `spellforge_sniffs` MySQL database.
    // Uses the same host/port/user/password as the rest of the app but
    // always connects to "spellforge_sniffs" — never to WorldDbName or
    // spellforge_tracker.
    // ======================================================================
    internal static class SpellForgeSniffsDb
    {
        // ------------------------------------------------------------------ //
        // Connection string — mirrors SpellForgeTrackerDb pattern exactly
        // ------------------------------------------------------------------ //

        private static string ConnStr
        {
            get
            {
                if (Settings.Default.Host == ".")
                    return $"Server=localhost;Pipe={Settings.Default.PortOrPipe};" +
                           $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                           $"Database=spellforge_sniffs;CharacterSet=utf8mb4;ConnectionTimeout=5;ConnectionProtocol=Pipe;";
                return $"Server={Settings.Default.Host};Port={Settings.Default.PortOrPipe};" +
                       $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                       $"Database=spellforge_sniffs;CharacterSet=utf8mb4;ConnectionTimeout=5;";
            }
        }

        // ------------------------------------------------------------------ //
        // Models
        // ------------------------------------------------------------------ //

        #region Models

        public sealed class SniffSessionInfo
        {
            public int      Id           { get; init; }
            public string   FileName     { get; init; } = string.Empty;
            public string   BuildVersion { get; init; } = string.Empty;
            public DateTime ImportedAt   { get; init; }
            public int      PacketCount  { get; init; }
            public string   Notes        { get; init; } = string.Empty;
        }

        public sealed class SniffSpellCast
        {
            public int    SessionId          { get; init; }
            public int    PacketNumber       { get; init; }
            public long   TimestampMs        { get; init; }
            public int    SpellId            { get; init; }
            public string CasterType         { get; init; } = string.Empty;
            public string CasterGuid         { get; init; } = string.Empty;
            public string TargetType         { get; init; } = string.Empty;
            public string TargetGuid         { get; init; } = string.Empty;
            public int    CastTimeMs         { get; init; }
            public int    CastFlags          { get; init; }
            public bool   IsTriggered        { get; init; }
            public int    HitCount           { get; init; }
            public int    MissCount          { get; init; }
            public int?   TriggeredBySpellId { get; init; }
        }

        public enum AuraEventType { Applied, Removed, Refreshed, StackChanged }

        public sealed class SniffAuraEvent
        {
            public int           SessionId    { get; init; }
            public int           PacketNumber { get; init; }
            public long          TimestampMs  { get; init; }
            public int           SpellId      { get; init; }
            public string        UnitType     { get; init; } = string.Empty;
            public string        UnitGuid     { get; init; } = string.Empty;
            public string        CasterType   { get; init; } = string.Empty;
            public string        CasterGuid   { get; init; } = string.Empty;
            public AuraEventType EventType    { get; init; }
            public int           Slot         { get; init; }
            public int           StackCount   { get; init; }
            public int           DurationMs   { get; init; }
            public int           RemainingMs  { get; init; }
            public int           AuraFlags    { get; init; }
        }

        public sealed class SniffDamageEvent
        {
            public int    SessionId    { get; init; }
            public int    PacketNumber { get; init; }
            public long   TimestampMs  { get; init; }
            public int    SpellId      { get; init; }
            public string CasterType   { get; init; } = string.Empty;
            public string CasterGuid   { get; init; } = string.Empty;
            public string TargetType   { get; init; } = string.Empty;
            public string TargetGuid   { get; init; } = string.Empty;
            public int    SchoolMask   { get; init; }
            public int    Damage       { get; init; }
            public int    Overkill     { get; init; }
            public int    Absorbed     { get; init; }
            public int    Resisted     { get; init; }
            public int    Blocked      { get; init; }
            public bool   IsPeriodic   { get; init; }
            public bool   IsCritical   { get; init; }
        }

        public sealed class SniffHealEvent
        {
            public int    SessionId    { get; init; }
            public int    PacketNumber { get; init; }
            public long   TimestampMs  { get; init; }
            public int    SpellId      { get; init; }
            public string CasterType   { get; init; } = string.Empty;
            public string CasterGuid   { get; init; } = string.Empty;
            public string TargetType   { get; init; } = string.Empty;
            public string TargetGuid   { get; init; } = string.Empty;
            public int    Heal         { get; init; }
            public int    Overheal     { get; init; }
            public int    Absorbed     { get; init; }
            public bool   IsPeriodic   { get; init; }
            public bool   IsCritical   { get; init; }
        }

        public sealed class SniffEnergizeEvent
        {
            public int    SessionId    { get; init; }
            public int    PacketNumber { get; init; }
            public long   TimestampMs  { get; init; }
            public int    SpellId      { get; init; }
            public string CasterType   { get; init; } = string.Empty;
            public string CasterGuid   { get; init; } = string.Empty;
            public string TargetType   { get; init; } = string.Empty;
            public string TargetGuid   { get; init; } = string.Empty;
            public int    PowerType    { get; init; }
            public int    Amount       { get; init; }
            public int    OverEnergize { get; init; }
        }

        public sealed class SniffPeriodicEvent
        {
            public int    SessionId    { get; init; }
            public int    PacketNumber { get; init; }
            public long   TimestampMs  { get; init; }
            public int    SpellId      { get; init; }
            public string CasterType   { get; init; } = string.Empty;
            public string CasterGuid   { get; init; } = string.Empty;
            public string TargetType   { get; init; } = string.Empty;
            public string TargetGuid   { get; init; } = string.Empty;
            public int    AuraType     { get; init; }
            public int    SchoolMask   { get; init; }
            public int    Amount       { get; init; }
            public int    OverAmount   { get; init; }
            public int    Absorbed     { get; init; }
            public bool   IsCritical   { get; init; }
        }

        public sealed class SniffSpellSummary
        {
            public int      SpellId          { get; init; }
            public int      TotalCasts       { get; init; }
            public int      InstantCastCount { get; init; }
            public int      TriggeredCount   { get; init; }
            public int      UniqueSessions   { get; init; }
            public double   AvgDamage        { get; init; }
            public int      MinDamage        { get; init; }
            public int      MaxDamage        { get; init; }
            public double   AvgHeal          { get; init; }
            public double   AvgEnergize      { get; init; }
            public int      AuraAppliedCount { get; init; }
            public int      AuraRemovedCount { get; init; }
            public string   LastSeenBuild    { get; init; } = string.Empty;
            public DateTime LastUpdated      { get; init; }
        }

        #endregion

        // ------------------------------------------------------------------ //
        // Schema bootstrap
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Reads <c>SQL/spellforge_sniffs_schema.sql</c> from the executable's
        /// directory and executes it statement-by-statement.
        /// Returns true on success.
        /// </summary>
        public static bool EnsureSchema()
        {
            try
            {
                // Connect without specifying the database first so the CREATE DATABASE works
                string baseConnStr;
                if (Settings.Default.Host == ".")
                    baseConnStr = $"Server=localhost;Pipe={Settings.Default.PortOrPipe};" +
                                  $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                                  $"CharacterSet=utf8mb4;ConnectionTimeout=5;ConnectionProtocol=Pipe;";
                else
                    baseConnStr = $"Server={Settings.Default.Host};Port={Settings.Default.PortOrPipe};" +
                                  $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                                  $"CharacterSet=utf8mb4;ConnectionTimeout=5;";

                string schemaPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SQL", "spellforge_sniffs_schema.sql");

                if (!File.Exists(schemaPath))
                    return false;

                string sql = File.ReadAllText(schemaPath, Encoding.UTF8);

                // Split on lines that are purely ";" — WPP-style separator — or on ";\n"
                // Simple approach: split on ";\n" and ";\r\n", trim, skip blanks/comments
                string[] rawStatements = sql.Split(new[] { ";\r\n", ";\n" }, StringSplitOptions.None);

                using var conn = new MySql.Data.MySqlClient.MySqlConnection(baseConnStr);
                conn.Open();

                foreach (string raw in rawStatements)
                {
                    string stmt = raw.Trim();
                    if (stmt.Length == 0) continue;
                    // Skip comment-only blocks
                    bool allComments = true;
                    foreach (string ln in stmt.Split('\n'))
                    {
                        string t = ln.Trim();
                        if (t.Length > 0 && !t.StartsWith("--") && !t.StartsWith("#"))
                        { allComments = false; break; }
                    }
                    if (allComments) continue;

                    using var cmd = new MySqlCommand(stmt, conn);
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ //
        // Session management
        // ------------------------------------------------------------------ //

        public static bool SessionExists(string fileName)
        {
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM `sniff_sessions` WHERE `file_name`=@f", conn);
                cmd.Parameters.AddWithValue("@f", fileName);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
            catch { return false; }
        }

        public static int InsertSession(string fileName, string buildVersion, int packetCount, string notes)
        {
            using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
            conn.Open();
            using var cmd = new MySqlCommand(
                "INSERT INTO `sniff_sessions` (`file_name`,`build_version`,`packet_count`,`notes`) " +
                "VALUES (@f,@b,@p,@n)", conn);
            cmd.Parameters.AddWithValue("@f", fileName);
            cmd.Parameters.AddWithValue("@b", buildVersion);
            cmd.Parameters.AddWithValue("@p", packetCount);
            cmd.Parameters.AddWithValue("@n", notes ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            return (int)cmd.LastInsertedId;
        }

        public static void UpdateSessionPacketCount(int sessionId, int count)
        {
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "UPDATE `sniff_sessions` SET `packet_count`=@c WHERE `id`=@id", conn);
                cmd.Parameters.AddWithValue("@c",  count);
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static List<SniffSessionInfo> GetAllSessions()
        {
            var list = new List<SniffSessionInfo>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT `id`,`file_name`,`build_version`,`imported_at`,`packet_count`,IFNULL(`notes`,'') " +
                    "FROM `sniff_sessions` ORDER BY `imported_at` DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SniffSessionInfo
                    {
                        Id           = r.GetInt32(0),
                        FileName     = r.GetString(1),
                        BuildVersion = r.GetString(2),
                        ImportedAt   = r.GetDateTime(3),
                        PacketCount  = r.GetInt32(4),
                        Notes        = r.GetString(5),
                    });
            }
            catch { }
            return list;
        }

        // ------------------------------------------------------------------ //
        // Bulk inserts — max 500 rows per statement
        // ------------------------------------------------------------------ //

        private const int BatchSize = 500;

        public static void BulkInsertCasts(int sessionId, IEnumerable<SniffSpellCast> rows)
        {
            BulkInsert(rows, sessionId, "sniff_spell_casts",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                "`cast_time_ms`,`cast_flags`,`is_triggered`,`hit_count`,`miss_count`,`triggered_by_spell_id`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.CasterType)}','{Esc(r.CasterGuid)}','{Esc(r.TargetType)}','{Esc(r.TargetGuid)}'," +
                     $"{r.CastTimeMs},{r.CastFlags},{(r.IsTriggered ? 1 : 0)}," +
                     $"{r.HitCount},{r.MissCount},{NullOrVal(r.TriggeredBySpellId)})");
        }

        public static void BulkInsertAuraEvents(int sessionId, IEnumerable<SniffAuraEvent> rows)
        {
            BulkInsert(rows, sessionId, "sniff_aura_events",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`unit_type`,`unit_guid`,`caster_type`,`caster_guid`," +
                "`event_type`,`slot`,`stack_count`,`duration_ms`,`remaining_ms`,`aura_flags`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.UnitType)}','{Esc(r.UnitGuid)}','{Esc(r.CasterType)}','{Esc(r.CasterGuid)}'," +
                     $"'{AuraEventTypeStr(r.EventType)}',{r.Slot},{r.StackCount},{r.DurationMs},{r.RemainingMs},{r.AuraFlags})");
        }

        public static void BulkInsertDamageEvents(int sessionId, IEnumerable<SniffDamageEvent> rows)
        {
            BulkInsert(rows, sessionId, "sniff_damage_events",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                "`school_mask`,`damage`,`overkill`,`absorbed`,`resisted`,`blocked`,`is_periodic`,`is_critical`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.CasterType)}','{Esc(r.CasterGuid)}','{Esc(r.TargetType)}','{Esc(r.TargetGuid)}'," +
                     $"{r.SchoolMask},{r.Damage},{r.Overkill},{r.Absorbed},{r.Resisted},{r.Blocked}," +
                     $"{(r.IsPeriodic ? 1 : 0)},{(r.IsCritical ? 1 : 0)})");
        }

        public static void BulkInsertHealEvents(int sessionId, IEnumerable<SniffHealEvent> rows)
        {
            BulkInsert(rows, sessionId, "sniff_heal_events",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                "`heal`,`overheal`,`absorbed`,`is_periodic`,`is_critical`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.CasterType)}','{Esc(r.CasterGuid)}','{Esc(r.TargetType)}','{Esc(r.TargetGuid)}'," +
                     $"{r.Heal},{r.Overheal},{r.Absorbed},{(r.IsPeriodic ? 1 : 0)},{(r.IsCritical ? 1 : 0)})");
        }

        public static void BulkInsertEnergizeEvents(int sessionId, IEnumerable<SniffEnergizeEvent> rows)
        {
            BulkInsert(rows, sessionId, "sniff_energize_events",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                "`power_type`,`amount`,`over_energize`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.CasterType)}','{Esc(r.CasterGuid)}','{Esc(r.TargetType)}','{Esc(r.TargetGuid)}'," +
                     $"{r.PowerType},{r.Amount},{r.OverEnergize})");
        }

        public static void BulkInsertPeriodicEvents(int sessionId, IEnumerable<SniffPeriodicEvent> rows)
        {
            BulkInsert(rows, sessionId, "sniff_periodic_events",
                "`session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                "`aura_type`,`school_mask`,`amount`,`over_amount`,`absorbed`,`is_critical`",
                r => $"({sessionId},{r.PacketNumber},{r.TimestampMs},{r.SpellId}," +
                     $"'{Esc(r.CasterType)}','{Esc(r.CasterGuid)}','{Esc(r.TargetType)}','{Esc(r.TargetGuid)}'," +
                     $"{r.AuraType},{r.SchoolMask},{r.Amount},{r.OverAmount},{r.Absorbed},{(r.IsCritical ? 1 : 0)})");
        }

        private static void BulkInsert<T>(
            IEnumerable<T> rows, int sessionId, string table, string columns,
            Func<T, string> rowSerializer)
        {
            var batch = new List<string>(BatchSize);

            void Flush()
            {
                if (batch.Count == 0) return;
                string sql = $"INSERT INTO `{table}` ({columns}) VALUES {string.Join(",", batch)}";
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                cmd.ExecuteNonQuery();
                batch.Clear();
            }

            foreach (T row in rows)
            {
                batch.Add(rowSerializer(row));
                if (batch.Count >= BatchSize) Flush();
            }
            Flush();
        }

        // ------------------------------------------------------------------ //
        // Summary refresh
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Recomputes <c>sniff_spell_summary</c> rows for the given spell IDs
        /// using REPLACE INTO from per-table aggregates.
        /// </summary>
        public static void RefreshSummaryForSpells(IEnumerable<int> spellIds)
        {
            var ids = new List<int>(spellIds);
            if (ids.Count == 0) return;

            // Build IN(...) list — these are integers, safe without parameterisation
            string inList = string.Join(",", ids);

            string sql = $@"
REPLACE INTO `sniff_spell_summary`
    (`spell_id`,`total_casts`,`instant_cast_count`,`triggered_count`,`unique_sessions`,
     `avg_damage`,`min_damage`,`max_damage`,`avg_heal`,`avg_energize`,
     `aura_applied_count`,`aura_removed_count`,`last_seen_build`)
SELECT
    s.spell_id,
    IFNULL(c.total_casts,    0),
    IFNULL(c.instant_count,  0),
    IFNULL(c.triggered_count,0),
    IFNULL(c.unique_sessions,0),
    IFNULL(d.avg_dmg,  0.0),
    IFNULL(d.min_dmg,  0),
    IFNULL(d.max_dmg,  0),
    IFNULL(h.avg_heal, 0.0),
    IFNULL(e.avg_en,   0.0),
    IFNULL(a.applied_count, 0),
    IFNULL(a.removed_count, 0),
    IFNULL(sess.build_version, '')
FROM (SELECT spell_id FROM (
        SELECT spell_id FROM `sniff_spell_casts`   WHERE spell_id IN ({inList})
        UNION
        SELECT spell_id FROM `sniff_damage_events` WHERE spell_id IN ({inList})
        UNION
        SELECT spell_id FROM `sniff_heal_events`   WHERE spell_id IN ({inList})
        UNION
        SELECT spell_id FROM `sniff_energize_events` WHERE spell_id IN ({inList})
        UNION
        SELECT spell_id FROM `sniff_aura_events`   WHERE spell_id IN ({inList})
     ) AS combined) AS s
LEFT JOIN (
    SELECT spell_id,
           COUNT(*)                                      AS total_casts,
           SUM(cast_time_ms = 0)                        AS instant_count,
           SUM(is_triggered)                             AS triggered_count,
           COUNT(DISTINCT session_id)                    AS unique_sessions
    FROM `sniff_spell_casts` WHERE spell_id IN ({inList}) GROUP BY spell_id
) AS c ON c.spell_id = s.spell_id
LEFT JOIN (
    SELECT spell_id,
           AVG(damage) AS avg_dmg,
           MIN(damage) AS min_dmg,
           MAX(damage) AS max_dmg
    FROM `sniff_damage_events` WHERE spell_id IN ({inList}) GROUP BY spell_id
) AS d ON d.spell_id = s.spell_id
LEFT JOIN (
    SELECT spell_id, AVG(heal) AS avg_heal
    FROM `sniff_heal_events` WHERE spell_id IN ({inList}) GROUP BY spell_id
) AS h ON h.spell_id = s.spell_id
LEFT JOIN (
    SELECT spell_id, AVG(amount) AS avg_en
    FROM `sniff_energize_events` WHERE spell_id IN ({inList}) GROUP BY spell_id
) AS e ON e.spell_id = s.spell_id
LEFT JOIN (
    SELECT spell_id,
           SUM(event_type = 'APPLIED')  AS applied_count,
           SUM(event_type = 'REMOVED')  AS removed_count
    FROM `sniff_aura_events` WHERE spell_id IN ({inList}) GROUP BY spell_id
) AS a ON a.spell_id = s.spell_id
LEFT JOIN (
    SELECT sc.spell_id, ss.build_version
    FROM `sniff_spell_casts` sc
    JOIN `sniff_sessions` ss ON ss.id = sc.session_id
    WHERE sc.spell_id IN ({inList})
    ORDER BY ss.imported_at DESC
    LIMIT 1
) AS sess ON sess.spell_id = s.spell_id;";

            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                cmd.CommandTimeout = 60;
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        // ------------------------------------------------------------------ //
        // Query helpers
        // ------------------------------------------------------------------ //

        /// <summary>Returns true when the spellforge_sniffs database is reachable.</summary>
        public static bool CanConnect()
        {
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns all distinct spell IDs that appear in <c>sniff_spell_casts</c>.
        /// Used to match sniff-captured cast data against DBC spell names.
        /// </summary>
        public static HashSet<int> GetAllCastSpellIds()
        {
            var ids = new HashSet<int>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT DISTINCT `spell_id` FROM `sniff_spell_casts`", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt32(0));
            }
            catch { }
            return ids;
        }

        /// <summary>
        /// Returns all distinct spell IDs that appear in any non-cast sniff event table
        /// (aura events, damage events, heal events, energize events, periodic events).
        /// Used to match sniff-captured event data against DBC spell names.
        /// </summary>
        public static HashSet<int> GetAllEventSpellIds()
        {
            var ids = new HashSet<int>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                const string sql =
                    "SELECT DISTINCT `spell_id` FROM `sniff_aura_events` " +
                    "UNION SELECT DISTINCT `spell_id` FROM `sniff_damage_events` " +
                    "UNION SELECT DISTINCT `spell_id` FROM `sniff_heal_events` " +
                    "UNION SELECT DISTINCT `spell_id` FROM `sniff_energize_events` " +
                    "UNION SELECT DISTINCT `spell_id` FROM `sniff_periodic_events`";
                using var cmd = new MySqlCommand(sql, conn);
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt32(0));
            }
            catch { }
            return ids;
        }

        public static SniffSpellSummary? GetSummary(int spellId)
        {
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT `spell_id`,`total_casts`,`instant_cast_count`,`triggered_count`," +
                    "`unique_sessions`,`avg_damage`,`min_damage`,`max_damage`," +
                    "`avg_heal`,`avg_energize`,`aura_applied_count`,`aura_removed_count`," +
                    "`last_seen_build`,`last_updated` " +
                    "FROM `sniff_spell_summary` WHERE `spell_id`=@id", conn);
                cmd.Parameters.AddWithValue("@id", spellId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                return new SniffSpellSummary
                {
                    SpellId          = r.GetInt32(0),
                    TotalCasts       = r.GetInt32(1),
                    InstantCastCount = r.GetInt32(2),
                    TriggeredCount   = r.GetInt32(3),
                    UniqueSessions   = r.GetInt32(4),
                    AvgDamage        = r.GetDouble(5),
                    MinDamage        = r.GetInt32(6),
                    MaxDamage        = r.GetInt32(7),
                    AvgHeal          = r.GetDouble(8),
                    AvgEnergize      = r.GetDouble(9),
                    AuraAppliedCount = r.GetInt32(10),
                    AuraRemovedCount = r.GetInt32(11),
                    LastSeenBuild    = r.GetString(12),
                    LastUpdated      = r.GetDateTime(13),
                };
            }
            catch { return null; }
        }

        public static List<SniffSpellCast> GetCastsForSpell(int spellId, int limit = 200)
        {
            var list = new List<SniffSpellCast>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT `session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                    "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                    "`cast_time_ms`,`cast_flags`,`is_triggered`,`hit_count`,`miss_count`,`triggered_by_spell_id` " +
                    "FROM `sniff_spell_casts` WHERE `spell_id`=@id ORDER BY `id` LIMIT @lim", conn);
                cmd.Parameters.AddWithValue("@id",  spellId);
                cmd.Parameters.AddWithValue("@lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SniffSpellCast
                    {
                        SessionId          = r.GetInt32(0),
                        PacketNumber       = r.GetInt32(1),
                        TimestampMs        = r.GetInt64(2),
                        SpellId            = r.GetInt32(3),
                        CasterType         = r.GetString(4),
                        CasterGuid         = r.GetString(5),
                        TargetType         = r.GetString(6),
                        TargetGuid         = r.GetString(7),
                        CastTimeMs         = r.GetInt32(8),
                        CastFlags          = r.GetInt32(9),
                        IsTriggered        = r.GetBoolean(10),
                        HitCount           = r.GetInt32(11),
                        MissCount          = r.GetInt32(12),
                        TriggeredBySpellId = r.IsDBNull(13) ? null : r.GetInt32(13),
                    });
            }
            catch { }
            return list;
        }

        public static List<SniffAuraEvent> GetAuraEventsForSpell(int spellId, int limit = 200)
        {
            var list = new List<SniffAuraEvent>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT `session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                    "`unit_type`,`unit_guid`,`caster_type`,`caster_guid`," +
                    "`event_type`,`slot`,`stack_count`,`duration_ms`,`remaining_ms`,`aura_flags` " +
                    "FROM `sniff_aura_events` WHERE `spell_id`=@id ORDER BY `id` LIMIT @lim", conn);
                cmd.Parameters.AddWithValue("@id",  spellId);
                cmd.Parameters.AddWithValue("@lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SniffAuraEvent
                    {
                        SessionId    = r.GetInt32(0),
                        PacketNumber = r.GetInt32(1),
                        TimestampMs  = r.GetInt64(2),
                        SpellId      = r.GetInt32(3),
                        UnitType     = r.GetString(4),
                        UnitGuid     = r.GetString(5),
                        CasterType   = r.GetString(6),
                        CasterGuid   = r.GetString(7),
                        EventType    = ParseAuraEventType(r.GetString(8)),
                        Slot         = r.GetInt32(9),
                        StackCount   = r.GetInt32(10),
                        DurationMs   = r.GetInt32(11),
                        RemainingMs  = r.GetInt32(12),
                        AuraFlags    = r.GetInt32(13),
                    });
            }
            catch { }
            return list;
        }

        public static List<SniffDamageEvent> GetDamageEventsForSpell(int spellId, int limit = 200)
        {
            var list = new List<SniffDamageEvent>();
            try
            {
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "SELECT `session_id`,`packet_number`,`timestamp_ms`,`spell_id`," +
                    "`caster_type`,`caster_guid`,`target_type`,`target_guid`," +
                    "`school_mask`,`damage`,`overkill`,`absorbed`,`resisted`,`blocked`,`is_periodic`,`is_critical` " +
                    "FROM `sniff_damage_events` WHERE `spell_id`=@id ORDER BY `id` LIMIT @lim", conn);
                cmd.Parameters.AddWithValue("@id",  spellId);
                cmd.Parameters.AddWithValue("@lim", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new SniffDamageEvent
                    {
                        SessionId    = r.GetInt32(0),
                        PacketNumber = r.GetInt32(1),
                        TimestampMs  = r.GetInt64(2),
                        SpellId      = r.GetInt32(3),
                        CasterType   = r.GetString(4),
                        CasterGuid   = r.GetString(5),
                        TargetType   = r.GetString(6),
                        TargetGuid   = r.GetString(7),
                        SchoolMask   = r.GetInt32(8),
                        Damage       = r.GetInt32(9),
                        Overkill     = r.GetInt32(10),
                        Absorbed     = r.GetInt32(11),
                        Resisted     = r.GetInt32(12),
                        Blocked      = r.GetInt32(13),
                        IsPeriodic   = r.GetBoolean(14),
                        IsCritical   = r.GetBoolean(15),
                    });
            }
            catch { }
            return list;
        }

        // ------------------------------------------------------------------ //
        // Private helpers
        // ------------------------------------------------------------------ //

        private static string Esc(string s) =>
            s.Replace("\\", "\\\\").Replace("'", "\\'");

        private static string NullOrVal(int? v) =>
            v.HasValue ? v.Value.ToString() : "NULL";

        private static string AuraEventTypeStr(AuraEventType t) => t switch
        {
            AuraEventType.Applied      => "APPLIED",
            AuraEventType.Removed      => "REMOVED",
            AuraEventType.Refreshed    => "REFRESHED",
            AuraEventType.StackChanged => "STACK_CHANGED",
            _                          => "APPLIED",
        };

        private static AuraEventType ParseAuraEventType(string s) => s switch
        {
            "REMOVED"      => AuraEventType.Removed,
            "REFRESHED"    => AuraEventType.Refreshed,
            "STACK_CHANGED"=> AuraEventType.StackChanged,
            _              => AuraEventType.Applied,
        };
    }
}
