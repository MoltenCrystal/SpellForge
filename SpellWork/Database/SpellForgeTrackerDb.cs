using MySql.Data.MySqlClient;
using SpellWork.DBC;
using SpellWork.Forms;
using SpellWork.Properties;
using System.Drawing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SpellWork.Database
{
    /// <summary>
    /// Read-only access to the <c>spellforge_tracker</c> MySQL database.
    /// Schema creation and data import are handled externally via manually-run SQL files.
    /// </summary>
    internal static class SpellForgeTrackerDb
    {
        // ------------------------------------------------------------------ //
        // Connection string
        // ------------------------------------------------------------------ //

        private static string ConnStr
        {
            get
            {
                if (Settings.Default.Host == ".")
                    return $"Server=localhost;Pipe={Settings.Default.PortOrPipe};" +
                           $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                           $"Database=spellforge_tracker;CharacterSet=utf8mb4;ConnectionTimeout=5;ConnectionProtocol=Pipe;";
                return $"Server={Settings.Default.Host};Port={Settings.Default.PortOrPipe};" +
                       $"UserID={Settings.Default.User};Password={Settings.Default.Pass};" +
                       $"Database=spellforge_tracker;CharacterSet=utf8mb4;ConnectionTimeout=5;";
            }
        }

        // ------------------------------------------------------------------ //
        // Stored-data models
        // ------------------------------------------------------------------ //

        public sealed class StoredNode
        {
            public string TreeType { get; init; } = string.Empty;
            public int    CellId   { get; init; }
            public int    RowPos   { get; init; }
            public int    ColPos   { get; init; }
            public int    MaxRank  { get; init; }
            public bool   IsGate   { get; init; }
            public int    SpellId  { get; init; }
            public string NodeName { get; init; } = string.Empty;
            public string IconName { get; init; } = string.Empty;
            public int    AltSpellId  { get; init; }
            public string AltIconName { get; init; } = string.Empty;
        }

        public sealed class StoredConnection
        {
            public string TreeType { get; init; } = string.Empty;
            public int    FromCell { get; init; }
            public int    ToCell   { get; init; }
        }

        public sealed class StoredSpell
        {
            public int    SpellId   { get; init; }
            public string SpellName { get; init; } = string.Empty;
            public string ClassName { get; init; } = string.Empty;
            public string SpecName  { get; init; } = string.Empty;
            public string HeroName  { get; init; } = string.Empty;
            public string TreeType  { get; init; } = string.Empty;
            public string Status    { get; init; } = "unknown";
            public string Notes     { get; init; } = string.Empty;
        }

        // ------------------------------------------------------------------ //
        // Reads
        // ------------------------------------------------------------------ //

        public static List<StoredNode> LoadNodes(string cls, string spec, string hero)
        {
            // mode 2 = icon_name + alt_spell_id + alt_icon_name
            // mode 1 = icon_name only
            // mode 0 = minimal (no optional columns)
            for (int mode = 2; mode >= 0; mode--)
            {
                var list = new List<StoredNode>();
                try
                {
                    using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                    c.Open();
                    string extraCols = mode switch
                    {
                        2 => ", IFNULL(icon_name,''), IFNULL(alt_spell_id,0), IFNULL(alt_icon_name,'')",
                        1 => ", IFNULL(icon_name,'')",
                        _ => string.Empty
                    };
                    using var cmd = new MySqlCommand(
                        "SELECT tree_type, cell_id, row_pos, col_pos, max_rank, is_gate, spell_id, node_name" +
                        extraCols +
                        " FROM talent_tree_nodes WHERE class_name=@c AND spec_name=@s " +
                        "AND (hero_name=@h OR hero_name='')", c);
                    cmd.Parameters.AddWithValue("@c", cls);
                    cmd.Parameters.AddWithValue("@s", spec);
                    cmd.Parameters.AddWithValue("@h", hero);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        list.Add(new StoredNode
                        {
                            TreeType    = r.GetString(0), CellId   = r.GetInt32(1),
                            RowPos      = r.GetInt32(2),  ColPos   = r.GetInt32(3),
                            MaxRank     = r.GetInt32(4),  IsGate   = r.GetBoolean(5),
                            SpellId     = r.GetInt32(6),  NodeName = r.GetString(7),
                            IconName    = mode >= 1 ? r.GetString(8)  : string.Empty,
                            AltSpellId  = mode >= 2 ? r.GetInt32(9)   : 0,
                            AltIconName = mode >= 2 ? r.GetString(10) : string.Empty,
                        });
                    return list;
                }
                catch (MySqlException ex) when (ex.Number == 1054 && mode > 0)
                {
                    continue;  // retry with fewer optional columns
                }
                catch { return new List<StoredNode>(); }
            }
            return new List<StoredNode>();
        }

        public static List<StoredConnection> LoadConnections(string cls, string spec, string hero)
        {
            var list = new List<StoredConnection>();
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using var cmd = new MySqlCommand(
                    "SELECT tree_type, from_cell, to_cell FROM talent_tree_connections " +
                    "WHERE class_name=@c AND spec_name=@s " +
                    "AND (hero_name=@h OR hero_name='')", c);
                cmd.Parameters.AddWithValue("@c", cls);
                cmd.Parameters.AddWithValue("@s", spec);
                cmd.Parameters.AddWithValue("@h", hero);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new StoredConnection
                    {
                        TreeType = r.GetString(0),
                        FromCell = r.GetInt32(1),
                        ToCell   = r.GetInt32(2),
                    });
            }
            catch { }
            return list;
        }

        public static List<StoredSpell> LoadSpells(string cls, string spec, string hero)
        {
            var list = new List<StoredSpell>();
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using var cmd = new MySqlCommand(
                    "SELECT spell_id, spell_name, class_name, spec_name, hero_name, " +
                    "tree_type, status, IFNULL(notes,'') " +
                    "FROM talent_spells WHERE class_name=@c AND spec_name=@s AND hero_name=@h", c);
                cmd.Parameters.AddWithValue("@c", cls);
                cmd.Parameters.AddWithValue("@s", spec);
                cmd.Parameters.AddWithValue("@h", hero);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new StoredSpell
                    {
                        SpellId   = r.GetInt32(0), SpellName = r.GetString(1),
                        ClassName = r.GetString(2), SpecName  = r.GetString(3),
                        HeroName  = r.GetString(4), TreeType  = r.GetString(5),
            Status    = r.GetString(6) is { Length: > 0 } s ? s : "nyi", Notes = r.GetString(7),
                    });
            }
            catch { }
            return list;
        }

        // ------------------------------------------------------------------ //
        // Schema / write operations (stubs � implement as needed)
        // ------------------------------------------------------------------ //

        public sealed class SyncResult
        {
            public bool   HasChanges    { get; init; }
            public string SqlFilePath   { get; init; } = string.Empty;
            public int    AddedNodes    { get; init; }
            public int    UpdatedNodes  { get; init; }
            public int    RemovedNodes  { get; init; }
            public int    AddedConns    { get; init; }
            public int    RemovedConns  { get; init; }
        }

        public static bool EnsureSchema()
        {
            return false;
        }

        public static void UpsertSpells(
            IEnumerable<(int SpellId, string SpellName, string ClassName,
                         string SpecName, string HeroName, string TreeType)> spells)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                foreach (var s in spells)
                {
                    if (s.SpellId <= 0) continue;
                    // INSERT IGNORE so existing status/notes written by the user are never overwritten.
                    using var cmd = new MySqlCommand(
                        "INSERT IGNORE INTO talent_spells " +
                        "(spell_id, spell_name, class_name, spec_name, hero_name, tree_type, status) " +
                        "VALUES (@sid, @sname, @cls, @spec, @hero, @tt, 'nyi')", c);
                    cmd.Parameters.AddWithValue("@sid",   s.SpellId);
                    cmd.Parameters.AddWithValue("@sname", s.SpellName);
                    cmd.Parameters.AddWithValue("@cls",   s.ClassName);
                    cmd.Parameters.AddWithValue("@spec",  s.SpecName);
                    cmd.Parameters.AddWithValue("@hero",  s.HeroName);
                    cmd.Parameters.AddWithValue("@tt",    s.TreeType);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SpellForgeTrackerDb] UpsertSpells failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Upserts all nodes from the three talent trees (class, hero, spec) into
        /// <c>talent_tree_nodes</c>.  Class and spec tree nodes are stored with
        /// <c>hero_name=''</c> so they are shared across hero selections; hero tree
        /// nodes use the explicit hero name.  Existing rows are updated in-place;
        /// user-managed columns (<c>icon_name</c>, <c>alt_spell_id</c>,
        /// <c>alt_icon_name</c>) are preserved.
        /// </summary>
        public static void UpsertNodes(
            IReadOnlyList<TalentTree> trees,
            string cls, string spec, string hero)
        {
            if (trees == null || trees.Count == 0) return;
            // s_treeTypes: [0]="class"  [1]="hero"  [2]="spec"
            // hero_name for each tree:  [0]=""  [1]=heroName  [2]=""
            string[] heroNames = ["", hero, ""];

            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();

                for (int i = 0; i < Math.Min(trees.Count, 3); i++)
                {
                    string treeType = s_treeTypes[i];
                    string heroName = heroNames[i];

                    foreach (var node in trees[i].Nodes)
                        {
                            using var cmd = new MySqlCommand(
                                "INSERT INTO talent_tree_nodes " +
                                "(class_name, spec_name, hero_name, tree_type, cell_id, " +
                                " row_pos, col_pos, max_rank, is_gate, spell_id, node_name, " +
                                " icon_name, alt_spell_id, alt_icon_name) " +
                                "VALUES (@cls, @spec, @hero, @tt, @cell, " +
                                "        @row, @col, @rank, @gate, @sid, @name, @icon, @asid, @aicon) " +
                                "ON DUPLICATE KEY UPDATE " +
                                "  row_pos=VALUES(row_pos), col_pos=VALUES(col_pos), " +
                                "  max_rank=VALUES(max_rank), " +
                                "  spell_id=VALUES(spell_id), node_name=VALUES(node_name), " +
                                "  icon_name=IF(VALUES(icon_name)!='', VALUES(icon_name), icon_name), " +
                                "  alt_spell_id=IF(VALUES(alt_spell_id)!=0, VALUES(alt_spell_id), alt_spell_id), " +
                                "  alt_icon_name=IF(VALUES(alt_icon_name)!='', VALUES(alt_icon_name), alt_icon_name)", c);
                            cmd.Parameters.AddWithValue("@cls",   cls);
                            cmd.Parameters.AddWithValue("@spec",  spec);
                            cmd.Parameters.AddWithValue("@hero",  heroName);
                            cmd.Parameters.AddWithValue("@tt",    treeType);
                            cmd.Parameters.AddWithValue("@cell",  node.Id);
                            cmd.Parameters.AddWithValue("@row",   node.Row);
                            cmd.Parameters.AddWithValue("@col",   (int)node.Col);
                            cmd.Parameters.AddWithValue("@rank",  node.MaxRank);
                            cmd.Parameters.AddWithValue("@gate",  node.IsGate ? 1 : 0);
                            cmd.Parameters.AddWithValue("@sid",   node.SpellId);
                            cmd.Parameters.AddWithValue("@name",  node.Name);
                            cmd.Parameters.AddWithValue("@icon",  node.IconName);
                            cmd.Parameters.AddWithValue("@asid",  node.AltSpellId);
                            cmd.Parameters.AddWithValue("@aicon", node.AltIconName);
                            cmd.ExecuteNonQuery();
                        }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SpellForgeTrackerDb] UpsertNodes failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Replaces all connections for this class/spec/hero with the parsed set.
        /// Class and spec connections use <c>hero_name=''</c>; hero connections use the
        /// explicit hero name.
        /// </summary>
        public static void UpsertConnections(
            IReadOnlyList<TalentTree> trees,
            string cls, string spec, string hero)
        {
            if (trees == null || trees.Count == 0) return;
            string[] heroNames = ["", hero, ""];

            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();

                // Delete and re-insert is safe because connections are derived
                // entirely from the parsed page — there are no user-editable fields.
                using (var del = new MySqlCommand(
                    "DELETE FROM talent_tree_connections " +
                    "WHERE class_name=@cls AND spec_name=@spec " +
                    "AND (hero_name=@hero OR hero_name='')", c))
                {
                    del.Parameters.AddWithValue("@cls",  cls);
                    del.Parameters.AddWithValue("@spec", spec);
                    del.Parameters.AddWithValue("@hero", hero);
                    del.ExecuteNonQuery();
                }

                for (int i = 0; i < Math.Min(trees.Count, 3); i++)
                {
                    string treeType = s_treeTypes[i];
                    string heroName = heroNames[i];

                    foreach (var node in trees[i].Nodes)
                    {
                        foreach (var childId in node.ChildIds)
                        {
                            using var cmd = new MySqlCommand(
                                "INSERT IGNORE INTO talent_tree_connections " +
                                "(class_name, spec_name, hero_name, tree_type, from_cell, to_cell) " +
                                "VALUES (@cls, @spec, @hero, @tt, @from, @to)", c);
                            cmd.Parameters.AddWithValue("@cls",  cls);
                            cmd.Parameters.AddWithValue("@spec", spec);
                            cmd.Parameters.AddWithValue("@hero", heroName);
                            cmd.Parameters.AddWithValue("@tt",   treeType);
                            cmd.Parameters.AddWithValue("@from", node.Id);
                            cmd.Parameters.AddWithValue("@to",   childId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SpellForgeTrackerDb] UpsertConnections failed: {ex.Message}");
                throw;
            }
        }

        public static SyncResult DiffAndApply(
            IReadOnlyList<TalentTree> trees,
            string cls, string spec, string hero,
            string outputDir)
        {
            return new SyncResult();
        }

        public static (int executed, int errors) ImportSqlFiles(string sqlDir)
        {
            return (0, 0);
        }

        public static string GenerateSchemaFile(string outputDir)
        {
            var path = Path.Combine(outputDir, "SQL", "00_schema.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
                File.WriteAllText(path, "-- schema placeholder\n");
            return path;
        }

        // ------------------------------------------------------------------ //
        // DB presence check and tree reconstruction
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Tests whether the <c>spellforge_tracker</c> database is reachable.
        /// Uses its own connection string – independent of the world-DB connection.
        /// </summary>
        public static bool CanConnect()
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Updates <c>node_name</c> in <c>talent_tree_nodes</c> and
        /// <c>spell_name</c> in <c>talent_spells</c> for every row whose
        /// stored name no longer matches the current DBC spell name.
        /// Called automatically by <see cref="TryLoadTrees"/> so the
        /// displayed tree always reflects the latest DBC data.
        /// </summary>
        public static void RefreshNamesFromDbc(string cls, string spec, string hero)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();

                // ── talent_tree_nodes.node_name ───────────────────────────
                var nodeUpdates = new List<(string newName, string treetype, int cellId)>();

                using (var cmd = new MySqlCommand(
                    "SELECT tree_type, cell_id, spell_id, node_name " +
                    "FROM talent_tree_nodes " +
                    "WHERE class_name=@c AND spec_name=@s " +
                    "AND (hero_name=@h OR hero_name='')", c))
                {
                    cmd.Parameters.AddWithValue("@c", cls);
                    cmd.Parameters.AddWithValue("@s", spec);
                    cmd.Parameters.AddWithValue("@h", hero);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        int spellId    = r.GetInt32(2);
                        string stored  = r.GetString(3);
                        if (spellId <= 0) continue;
                        if (!DBC.DBC.SpellInfoStore.TryGetValue(spellId, out var si)) continue;
                        string fresh = si.Name;
                        if (!string.IsNullOrEmpty(fresh) && fresh != stored)
                            nodeUpdates.Add((fresh, r.GetString(0), r.GetInt32(1)));
                    }
                }

                foreach (var (newName, treetype, cellId) in nodeUpdates)
                {
                    using var upd = new MySqlCommand(
                        "UPDATE talent_tree_nodes SET node_name=@n " +
                        "WHERE tree_type=@t AND cell_id=@id " +
                        "AND class_name=@c AND spec_name=@s " +
                        "AND (hero_name=@h OR hero_name='')", c);
                    upd.Parameters.AddWithValue("@n",  newName);
                    upd.Parameters.AddWithValue("@t",  treetype);
                    upd.Parameters.AddWithValue("@id", cellId);
                    upd.Parameters.AddWithValue("@c",  cls);
                    upd.Parameters.AddWithValue("@s",  spec);
                    upd.Parameters.AddWithValue("@h",  hero);
                    upd.ExecuteNonQuery();
                }

                // ── talent_spells.spell_name ──────────────────────────────
                var spellUpdates = new List<(string newName, int spellId)>();

                using (var cmd2 = new MySqlCommand(
                    "SELECT spell_id, spell_name " +
                    "FROM talent_spells " +
                    "WHERE class_name=@c AND spec_name=@s AND hero_name=@h", c))
                {
                    cmd2.Parameters.AddWithValue("@c", cls);
                    cmd2.Parameters.AddWithValue("@s", spec);
                    cmd2.Parameters.AddWithValue("@h", hero);
                    using var r2 = cmd2.ExecuteReader();
                    while (r2.Read())
                    {
                        int spellId   = r2.GetInt32(0);
                        string stored = r2.GetString(1);
                        if (spellId <= 0) continue;
                        if (!DBC.DBC.SpellInfoStore.TryGetValue(spellId, out var si)) continue;
                        string fresh = si.Name;
                        if (!string.IsNullOrEmpty(fresh) && fresh != stored)
                            spellUpdates.Add((fresh, spellId));
                    }
                }

                foreach (var (newName, spellId) in spellUpdates)
                {
                    using var upd2 = new MySqlCommand(
                        "UPDATE talent_spells SET spell_name=@n " +
                        "WHERE spell_id=@id AND class_name=@c AND spec_name=@s AND hero_name=@h", c);
                    upd2.Parameters.AddWithValue("@n",  newName);
                    upd2.Parameters.AddWithValue("@id", spellId);
                    upd2.Parameters.AddWithValue("@c",  cls);
                    upd2.Parameters.AddWithValue("@s",  spec);
                    upd2.Parameters.AddWithValue("@h",  hero);
                    upd2.ExecuteNonQuery();
                }
            }
            catch { /* non-fatal – tree loading continues with stored names */ }
        }

        public static (string status, string notes) GetSpellStatusNotes(int spellId)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using var cmd = new MySqlCommand(
                    "SELECT IFNULL(status,'unknown'), IFNULL(notes,'') " +
                    "FROM talent_spells WHERE spell_id=@id LIMIT 1", c);
                cmd.Parameters.AddWithValue("@id", spellId);
                using var r = cmd.ExecuteReader();
            if (r.Read()) return (r.GetString(0) is { Length: > 0 } s ? s : "nyi", r.GetString(1));
            }
            catch { }
            return ("nyi", string.Empty);
        }

        public static void UpdateSpellStatusNotes(int spellId, string status, string notes)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using var cmd = new MySqlCommand(
                    "UPDATE talent_spells SET status=@s, notes=@n WHERE spell_id=@id", c);
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@n", notes);
                cmd.Parameters.AddWithValue("@id", spellId);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static bool HasData(string cls, string spec, string hero)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                // When a specific hero name is given, check only the hero-tree rows for
                // that hero.  Using "OR hero_name=''" would incorrectly match the shared
                // class/spec rows written by a *different* hero's import and cause the
                // second hero option for each spec to always be skipped.
                string sql = string.IsNullOrEmpty(hero)
                    ? "SELECT COUNT(*) FROM `talent_tree_nodes` " +
                      "WHERE `class_name`=@c AND `spec_name`=@s AND `hero_name`=''"
                    : "SELECT COUNT(*) FROM `talent_tree_nodes` " +
                      "WHERE `class_name`=@c AND `spec_name`=@s " +
                      "AND `hero_name`=@h AND `tree_type`='hero'";
                using var cmd = new MySqlCommand(sql, c);
                cmd.Parameters.AddWithValue("@c", cls);
                cmd.Parameters.AddWithValue("@s", spec);
                cmd.Parameters.AddWithValue("@h", hero);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch { return false; }
        }

        /// <summary>Returns true if any talent tree nodes exist in the database at all.</summary>
        public static bool HasAnyData()
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using var cmd = new MySqlCommand(
                    "SELECT 1 FROM `talent_tree_nodes` LIMIT 1", c);
                using var r = cmd.ExecuteReader();
                return r.Read();
            }
            catch { return false; }
        }

        /// <summary>
        /// Deletes all nodes and connections for the given class/spec/hero combination.
        /// Talent spells are intentionally preserved (they carry user-edited status/notes).
        /// </summary>
        public static void DeleteData(string cls, string spec, string hero)
        {
            try
            {
                using var c = new MySql.Data.MySqlClient.MySqlConnection(ConnStr);
                c.Open();
                using (var cmd = new MySqlCommand(
                    "DELETE FROM `talent_tree_connections` " +
                    "WHERE `class_name`=@c AND `spec_name`=@s " +
                    "AND (`hero_name`=@h OR `hero_name`='')", c))
                {
                    cmd.Parameters.AddWithValue("@c", cls);
                    cmd.Parameters.AddWithValue("@s", spec);
                    cmd.Parameters.AddWithValue("@h", hero);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new MySqlCommand(
                    "DELETE FROM `talent_tree_nodes` " +
                    "WHERE `class_name`=@c AND `spec_name`=@s " +
                    "AND (`hero_name`=@h OR `hero_name`='')", c))
                {
                    cmd.Parameters.AddWithValue("@c", cls);
                    cmd.Parameters.AddWithValue("@s", spec);
                    cmd.Parameters.AddWithValue("@h", hero);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SpellForgeTrackerDb] DeleteData failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Human-readable summary of the last <see cref="TryLoadTrees"/> icon-loading
        /// attempt.  Updated on every call so the UI can surface it immediately.
        /// </summary>
        public static string LastIconDiagnostic { get; private set; } = string.Empty;

        // tree-type labels: [0]=class [1]=hero [2]=spec
        private static readonly string[] s_treeTypes = ["class", "hero", "spec"];

        /// <summary>
        /// Walks up the directory tree from <see cref="AppContext.BaseDirectory"/> until it
        /// finds a <c>SQL\<paramref name="relativePath"/></c> sub-directory, or falls back
        /// to the expected production path so callers always get a non-null string.
        /// </summary>
        private static string FindSqlSubDir(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "SQL", relativePath);
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            // Fallback: production layout (SQL folder sits next to the exe)
            return Path.Combine(AppContext.BaseDirectory, "SQL", relativePath);
        }

        /// <summary>
        /// Reconstructs the three <see cref="TalentTree"/> objects ([0] class, [1] hero, [2] spec)
        /// from the database. Returns <c>null</c> when no nodes are stored for this combination.
        /// </summary>
        public static TalentTree[]? TryLoadTrees(string cls, string spec, string hero)
        {
            var storedNodes = LoadNodes(cls, spec, hero);
            if (storedNodes.Count == 0) return null;

            // Silently update any node/spell names in the DB that have drifted
            // from the current DBC data (e.g. after a game patch renames a spell).
            RefreshNamesFromDbc(cls, spec, hero);

            var storedConns = LoadConnections(cls, spec, hero);
            var connLookup  = storedConns
                .GroupBy(c => (c.TreeType, c.FromCell))
                .ToDictionary(g => g.Key, g => g.Select(c => c.ToCell).ToList());

            var treeNames  = new[] { cls, hero, spec };

            // Build a prioritised list of directories to search for icon files.
            // 1. New canonical path: SQL\talentIcons\<class>\ (PS1 v2+)
            // 2. Legacy path: talentTracker\classSpells\<class>\ (PS1 v1)
            // The upward search means both dev (bin\Debug\...) and production layouts work.
            var clsLower = cls.ToLowerInvariant();
            var iconDirs = new List<string>();
            string primaryDir = FindSqlSubDir(Path.Combine("talentIcons", clsLower));
            iconDirs.Add(primaryDir);  // always add (may not exist yet � checked per-file)

            // Legacy fallback: walk up looking for talentTracker\classSpells\<class>
            var legacyDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (legacyDir != null)
            {
                var candidate = Path.Combine(legacyDir.FullName, "talentTracker", "classSpells", clsLower);
                if (Directory.Exists(candidate)) { iconDirs.Add(candidate); break; }
                legacyDir = legacyDir.Parent;
            }

            int totalWithName = storedNodes.Count(n => !string.IsNullOrEmpty(n.IconName));
            Trace.WriteLine(
                $"[TalentIcons] {cls}/{spec}/{hero}: {storedNodes.Count} nodes, " +
                $"{totalWithName} have icon_name in DB.");
            foreach (var d in iconDirs)
                Trace.WriteLine($"[TalentIcons]   candidate dir: \"{d}\"  exists={Directory.Exists(d)}");

            var result = new TalentTree[3];

            for (int i = 0; i < 3; i++)
            {
                var tt      = s_treeTypes[i];
                var treeRaw = storedNodes.Where(n => n.TreeType == tt).ToList();

                // Normalize row indices to compact 0-based values (shared by all tree types).
                var rowMap = treeRaw.Select(n => n.RowPos).Distinct()
                                    .OrderBy(x => x)
                                    .Select((r, idx) => (r, idx))
                                    .ToDictionary(x => x.r, x => x.idx);

                // Column normalization strategy:
                //   Hero tree  � per-row centering: each row's nodes are packed to 0,1,2,�
                //                and shorter rows are centred relative to the widest row.
                //                This prevents the empty-slot gap that appears when the
                //                top/bottom single node forces a centre column that other
                //                rows don't use.
                //   Class/spec � global compact mapping: preserves lateral alignment between
                //                rows (important for the irregular class/spec shapes).
                Dictionary<int, float> colByCell;
                if (tt == "hero" && treeRaw.Count > 0)
                {
                    var byRow     = treeRaw.GroupBy(n => n.RowPos).OrderBy(g => g.Key).ToList();
                    int maxInRow  = byRow.Max(g => g.Count());
                    colByCell     = new Dictionary<int, float>();
                    foreach (var rowGroup in byRow)
                    {
                        var sorted   = rowGroup.OrderBy(n => n.ColPos).ToList();
                        int m        = sorted.Count;
                        float start  = (maxInRow - 1) / 2.0f - (m - 1) / 2.0f;
                        for (int j = 0; j < m; j++)
                            colByCell[sorted[j].CellId] = start + j;
                    }
                }
                else
                {
                    var colMap = treeRaw.Select(n => n.ColPos).Distinct()
                                        .OrderBy(x => x)
                                        .Select((c, idx) => (c, idx))
                                        .ToDictionary(x => x.c, x => x.idx);
                    colByCell  = treeRaw.ToDictionary(
                        n => n.CellId,
                        n => (float)colMap.GetValueOrDefault(n.ColPos, n.ColPos));
                }

                var nodes = treeRaw
                    .Select(n =>
                    {
                        // Resolve alt spell name from DBC if available
                        string altName = string.Empty;
                        if (n.AltSpellId > 0 && DBC.DBC.SpellInfoStore.TryGetValue(n.AltSpellId, out var altSpell))
                            altName = altSpell.Name;

                        var node = new TalentNode
                        {
                            Id         = n.CellId,
                            Col        = colByCell.GetValueOrDefault(n.CellId, 0f),
                            Row        = rowMap.GetValueOrDefault(n.RowPos, n.RowPos),
                            MaxRank    = n.MaxRank,
                            IsGate     = n.IsGate,
                            SpellId    = n.SpellId,
                            Name       = n.NodeName,
                            AltSpellId = n.AltSpellId,
                            AltName    = altName,
                        };
                        if (!string.IsNullOrEmpty(n.IconName))
                        {
                            foreach (var dir in iconDirs)
                            {
                                var iconPath = Path.Combine(dir, n.IconName);
                                if (!File.Exists(iconPath)) continue;
                                try
                                {
                                    using var tmp = Image.FromFile(iconPath);
                                    node.Icon = new Bitmap(tmp.Width, tmp.Height,
                                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                    using var gfx = Graphics.FromImage(node.Icon);
                                    gfx.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine($"[TalentIcons] Failed \"{iconPath}\": {ex.Message}");
                                }
                                break;  // stop at first found
                            }
                            if (node.Icon == null)
                                Trace.WriteLine($"[TalentIcons] Not found in any dir: {n.IconName}");
                        }
                        if (!string.IsNullOrEmpty(n.AltIconName))
                        {
                            foreach (var dir in iconDirs)
                            {
                                var altIconPath = Path.Combine(dir, n.AltIconName);
                                if (!File.Exists(altIconPath)) continue;
                                try
                                {
                                    using var tmp = Image.FromFile(altIconPath);
                                    node.AltIcon = new Bitmap(tmp.Width, tmp.Height,
                                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                    using var gfx = Graphics.FromImage(node.AltIcon);
                                    gfx.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine($"[TalentIcons] AltIcon failed \"{altIconPath}\": {ex.Message}");
                                }
                                break;
                            }
                        }
                        if (connLookup.TryGetValue((tt, n.CellId), out var children))
                            node.ChildIds.AddRange(children);
                        return node;
                    })
                    .ToList();

                result[i] = new TalentTree { Name = treeNames[i], Nodes = nodes };
            }

            int iconsLoaded = result.Sum(t => t?.Nodes.Count(n => n.Icon != null) ?? 0);
            LastIconDiagnostic = totalWithName == 0
                ? $"? 0/{storedNodes.Count} nodes have icon_name in DB � re-run parse_raw.ps1"
                : !Directory.Exists(primaryDir)
                    ? $"? Icon dir not found: {primaryDir} � re-run parse_raw.ps1 to download icons"
                    : iconsLoaded == 0
                        ? $"? Dir found but 0 icons loaded � check {primaryDir}"
                        : $"? {iconsLoaded}/{totalWithName} icons loaded from {primaryDir}";
            Trace.WriteLine($"[TalentIcons] {LastIconDiagnostic}");
            return result;
        }
    }
}
