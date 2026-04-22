using SpellWork.Database;
using SpellWork.Extensions;
using SpellWork.Filtering;
using SpellWork.Spell;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpellWork.Forms
{
    public sealed partial class FormMain : Form
    {
        private TalentTreeControl?        _talentTreeControl;
        private ToolStripStatusLabel? _lblTrackerStatus;

        // Currently-selected talent tree identity
        private string _currentClassName = string.Empty;
        private string _currentSpecName  = string.Empty;
        private string _currentHeroName  = string.Empty;

        // Optional UI controls (may be null if not present in designer)
        private Label?  _lblWowheadStatus;
        private Button? _bImportSql;
        private Button? _bGenerateSql;

        public FormMain()
        {
            InitializeComponent();
            splitContainer3.SplitterDistance = 200;

            Text = DBC.DBC.Version;

            _cbSpellFamilyName.SetEnumValues<SpellFamilyNames>("SpellFamilyName");
            _cbSpellAura.SetEnumValues<AuraType>("Aura");
            _cbSpellEffect.SetEnumValues<SpellEffects>("Effect");
            _cbTarget1.SetEnumValues<Targets>("Target A");
            _cbTarget2.SetEnumValues<Targets>("Target B");

            _cbProcSpellFamilyName.SetEnumValues<SpellFamilyNames>("SpellFamilyName");
            _cbProcSpellAura.SetEnumValues<AuraType>("Aura");
            _cbProcSpellEffect.SetEnumValues<SpellEffects>("Effect");
            _cbProcTarget1.SetEnumValues<Targets>("Target A");
            _cbProcTarget2.SetEnumValues<Targets>("Target B");

            _cbProcSpellFamilyTree.SetEnumValues<SpellFamilyNames>("SpellFamilyTree");
            _cbProcFitstSpellFamily.SetEnumValues<SpellFamilyNames>("SpellFamilyName");

            _clbSchools.SetFlags(new[]
            {
                SpellSchoolMask.SPELL_SCHOOL_MASK_NORMAL,
                SpellSchoolMask.SPELL_SCHOOL_MASK_HOLY,
                SpellSchoolMask.SPELL_SCHOOL_MASK_FIRE,
                SpellSchoolMask.SPELL_SCHOOL_MASK_NATURE,
                SpellSchoolMask.SPELL_SCHOOL_MASK_FROST,
                SpellSchoolMask.SPELL_SCHOOL_MASK_SHADOW,
                SpellSchoolMask.SPELL_SCHOOL_MASK_ARCANE
            }, "SPELL_SCHOOL_MASK_");
            _clbProcFlags.SetFlags<ProcFlags>("PROC_FLAG_");
            _clbProcFlags.AddFlags<ProcFlags2>("PROC_FLAG_2_", 1);
            _clbSpellTypeMask.SetFlags(new[]
            {
                ProcFlagsSpellType.PROC_SPELL_TYPE_DAMAGE,
                ProcFlagsSpellType.PROC_SPELL_TYPE_HEAL,
                ProcFlagsSpellType.PROC_SPELL_TYPE_NO_DMG_HEAL
            }, "PROC_SPELL_TYPE_");
            _clbSpellPhaseMask.SetFlags(new[]
            {
                ProcFlagsSpellPhase.PROC_SPELL_PHASE_CAST,
                ProcFlagsSpellPhase.PROC_SPELL_PHASE_HIT,
                ProcFlagsSpellPhase.PROC_SPELL_PHASE_FINISH
            }, "PROC_SPELL_PHASE_");
            _clbProcFlagHit.SetFlags<ProcFlagsHit>("PROC_HIT_");
            _clbProcAttributes.SetFlags<ProcAttributes>("PROC_ATTR_");

            _cbSqlSpellFamily.SetEnumValues<SpellFamilyNames>("SpellFamilyName");

            _cbAdvancedFilter1.SetStructFields<SpellInfo>();
            _cbAdvancedFilter2.SetStructFields<SpellInfo>();

            _cbAdvancedEffectFilter1.SetStructFields<SpellEffectInfo>();
            _cbAdvancedEffectFilter2.SetStructFields<SpellEffectInfo>();

            _cbAdvancedFilter1CompareType.SetEnumValuesDirect<CompareType>(true);
            _cbAdvancedFilter2CompareType.SetEnumValuesDirect<CompareType>(true);

            _cbAdvancedEffectFilter1CompareType.SetEnumValuesDirect<CompareType>(true);
            _cbAdvancedEffectFilter2CompareType.SetEnumValuesDirect<CompareType>(true);

            RefreshConnectionStatus();

            // SpellHell Tracker – status bar (in main StatusStrip) + talent tree canvas
            _tpSpellHellTracker.BackColor = Color.FromArgb(12, 12, 18);

            _lblTrackerStatus = new ToolStripStatusLabel
            {
                Spring    = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(160, 160, 170),
                Text      = "Select a class → spec → hero talent from the icon bar above.",
                Overflow  = ToolStripItemOverflow.Never,
            };
            statusStrip1.Items.Add(_lblTrackerStatus);

            _talentTreeControl = new TalentTreeControl { Dock = DockStyle.Fill };
            // Start with no trees – they are populated from the DB after a class/spec/hero is selected.
            _talentTreeControl.SetTrees(Array.Empty<TalentTree>());
            _talentTreeControl.ClassSpecHeroSelected  += OnClassSpecHeroSelected;
            _talentTreeControl.NodeOpenInSpellInfo    += OnNodeOpenInSpellInfo;
            _talentTreeControl.NodeStatusNotesUpdated += OnNodeStatusNotesUpdated;
            _talentTreeControl.QuerySpellStatusNotes   = spellId =>
                Database.SpellHellTrackerDb.GetSpellStatusNotes(spellId);
            _talentTreeControl.QueryImplementationSpells = spellId =>
                GetImplementationSpells(spellId);
            _talentTreeControl.QuerySummonedCreatureCasts = creatureEntry =>
                _spellHellParserSummonedCasts.TryGetValue(creatureEntry, out var ids)
                    ? (IReadOnlyCollection<int>)ids
                    : Array.Empty<int>();
            _tpSpellHellTracker.Controls.Add(_talentTreeControl);

            // Class filter ComboBox for the SpellHell Parser lists.
            // Positioned to the right of the Parse button (x=555+75+18 = 648).
            _cbSpellHellParserClassFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location      = new Point(648, 7),
                Width         = 140,
                FlatStyle     = FlatStyle.Standard,
                TabIndex      = 10,
            };
            _cbSpellHellParserClassFilter.Items.Add("All Classes");
            foreach (var (name, _) in s_parserClassFilters)
                _cbSpellHellParserClassFilter.Items.Add(name);
            _cbSpellHellParserClassFilter.SelectedIndex = 0;
            _cbSpellHellParserClassFilter.SelectedIndexChanged += SpellHellParserClassFilterChanged;
            _tpSpellHellParser.Controls.Add(_cbSpellHellParserClassFilter);
        }

        #region FORM

        private void ExitClick(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void ExtractEnumsClick(object sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog
            {
                Description      = "Select the folder to search for wsp_*.h files (all subfolders will be scanned)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = false,
            };

            if (fbd.ShowDialog(this) != DialogResult.OK)
                return;

            var selectedDir = fbd.SelectedPath;

            // Search recursively for all wsp_*.h files under the selected folder
            var headerFiles = Directory.GetFiles(selectedDir, "wsp_*.h", SearchOption.AllDirectories);
            if (headerFiles.Length == 0)
            {
                MessageBox.Show(
                    "No wsp_*.h files found in the selected folder or any of its subfolders.\n\n" +
                    $"Searched: {selectedDir}\n\n" +
                    "Please select the folder that contains (or has subfolders containing) the wsp_*.h files.",
                    "No Files Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Regex: matches   SOME_CONSTANT = 12345,   (optional trailing comment)
            var entryRegex = new System.Text.RegularExpressions.Regex(
                @"^\s*([A-Z0-9_]+)\s*=\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            // Regex: matches   enum SomeName
            var enumRegex  = new System.Text.RegularExpressions.Regex(
                @"^\s*enum\s+(\w+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            int totalFilesWritten = 0;
            int totalSpells       = 0;
            int totalNotFound     = 0;

            foreach (var headerPath in headerFiles)
            {
                var baseName   = Path.GetFileNameWithoutExtension(headerPath);
                var outputRoot = Path.Combine(AppContext.BaseDirectory, "WSP DATA", baseName);
                Directory.CreateDirectory(outputRoot);

                var lines = File.ReadAllLines(headerPath);

                string?  currentEnumName = null;
                var      currentEntries  = new List<(string ConstName, int SpellId)>();
                int      filesWritten    = 0;
                int      spellsFound     = 0;
                int      notFound        = 0;
                var      allFileSpells   = new List<(string ConstName, int SpellId, SpellInfo Info)>();

                void FlushEnum()
                {
                    if (currentEnumName == null || currentEntries.Count == 0)
                        return;

                    var tempRtb   = new RichTextBox();
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    foreach (var (constName, spellId) in currentEntries)
                    {
                        if (!DBC.DBC.SpellInfoStore.TryGetValue(spellId, out var spellInfo))
                        {
                            notFound++;
                            continue;
                        }

                        var sb = new StringBuilder();
                        sb.AppendLine($"// Extracted from {baseName}.h — enum {currentEnumName}");
                        sb.AppendLine($"// {constName} = {spellId}");
                        sb.AppendLine($"// Generated by SpellWork on {timestamp}");
                        sb.AppendLine();

                        tempRtb.Clear();
                        spellInfo.Write(tempRtb);
                        sb.Append(tempRtb.Text);

                        var outFile = Path.Combine(outputRoot, $"{constName}.txt");
                        File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
                        filesWritten++;
                        spellsFound++;
                        allFileSpells.Add((constName, spellId, spellInfo));
                    }
                    tempRtb.Dispose();

                    currentEntries.Clear();
                    currentEnumName = null;
                }

                foreach (var line in lines)
                {
                    var em = enumRegex.Match(line);
                    if (em.Success)
                    {
                        FlushEnum();
                        var name = em.Groups[1].Value;
                        currentEnumName = name.Contains("NPC", StringComparison.OrdinalIgnoreCase)
                            ? null : name;
                        continue;
                    }

                    if (line.TrimStart().StartsWith("}"))
                    {
                        FlushEnum();
                        continue;
                    }

                    if (currentEnumName == null)
                        continue;

                    var em2 = entryRegex.Match(line);
                    if (em2.Success && int.TryParse(em2.Groups[2].Value, out var id))
                        currentEntries.Add((em2.Groups[1].Value, id));
                }

                FlushEnum();

                // ── SPELL_PROC_<BASENAME>.txt ─────────────────────────────────────────────
                // Determine which SpellFamilyName(s) this header belongs to by looking at
                // the resolved spells, then dump ALL spells for those families from the full
                // DBC — grouped by SpellClassMask — so it covers every flag combination,
                // not just the subset referenced in the header file.
                if (allFileSpells.Count > 0)
                {
                    // Collect the dominant family names from the header's resolved spells,
                    // excluding SPELLFAMILY_GENERIC (0) which is not useful for proc masks.
                    var relevantFamilies = allFileSpells
                        .Select(e => e.Info.SpellFamilyName)
                        .Where(f => f != 0)
                        .Distinct()
                        .OrderBy(f => f)
                        .ToList();

                    if (relevantFamilies.Count > 0)
                    {
                        var procSb    = new StringBuilder();
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                        procSb.AppendLine($"// SPELL_PROC_{baseName.ToUpperInvariant()}");
                        procSb.AppendLine($"// Generated by SpellWork on {timestamp}");
                        procSb.AppendLine($"// Source: {Path.GetFileName(headerPath)}");
                        procSb.AppendLine($"// Full DBC SpellFamilyFlags dump for families detected in this header.");

                        foreach (var family in relevantFamilies)
                        {
                            var familyName = Enum.IsDefined(typeof(SpellFamilyNames), (int)family)
                                ? ((SpellFamilyNames)family).ToString()
                                : "SPELLFAMILY_UNKNOWN";

                            // Pull every spell in this family that has at least one non-zero
                            // SpellClassMask word — these are the only ones relevant to spell_proc.
                            var familySpells = DBC.DBC.SpellInfoStore.Values
                                .Where(s => s.SpellFamilyName == family &&
                                            s.SpellClassMask.Any(m => m != 0))
                                .OrderBy(s => s.ID)
                                .ToList();

                            procSb.AppendLine();
                            procSb.AppendLine($"// ============================================================");
                            procSb.AppendLine($"// SpellFamilyName: {familyName} ({family})");
                            procSb.AppendLine($"// Total spells with family flags: {familySpells.Count}");
                            procSb.AppendLine($"// ============================================================");

                            // Group by the exact 4-word mask key so every unique flag
                            // combination gets its own labelled block.
                            var byMask = familySpells
                                .GroupBy(s =>
                                {
                                    var m = s.SpellClassMask;
                                    return $"{m[0]:X8}{m[1]:X8}{m[2]:X8}{m[3]:X8}";
                                })
                                .OrderBy(g => g.Key);

                            foreach (var maskGroup in byMask)
                            {
                                var mask = maskGroup.First().SpellClassMask;
                                procSb.AppendLine();
                                procSb.AppendLine($"// [0x{mask[0]:X8} 0x{mask[1]:X8} 0x{mask[2]:X8} 0x{mask[3]:X8}]");
                                foreach (var s in maskGroup)
                                    procSb.AppendLine($"//   ({s.ID,-8}) {s.Name}");
                            }
                        }

                        var procFile = Path.Combine(outputRoot, $"SPELL_PROC_{baseName.ToUpperInvariant()}.txt");
                        File.WriteAllText(procFile, procSb.ToString(), Encoding.UTF8);
                    }
                }

                totalFilesWritten += filesWritten;
                totalSpells       += spellsFound;
                totalNotFound     += notFound;
            }

            MessageBox.Show(
                $"Done.\n\n" +
                $"Headers processed : {headerFiles.Length}\n" +
                $"Files written      : {totalFilesWritten}\n" +
                $"Spells found       : {totalSpells}\n" +
                $"Not in DBC         : {totalNotFound}\n\n" +
                $"Output folder:\n{Path.Combine(AppContext.BaseDirectory, "WSP DATA")}",
                "Extract Enums",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void TabControl1SelectedIndexChanged(object sender, EventArgs e)
        {
            _cbProcFlag.Visible = _bWrite.Visible = ((TabControl)sender).SelectedIndex == 3;
        }

        private void SettingsClick(object sender, EventArgs e)
        {
            var frm = new FormSettings();
            frm.ShowDialog(this);
            RefreshConnectionStatus();
        }

        private void FormMainResize(object sender, EventArgs e)
        {
            try
            {
                _scCompareRoot.SplitterDistance = (((Form)sender).Size.Width / 2) - 25;
                _chDescription.Width = (((Form)sender).Size.Width - 306);
            }
            // ReSharper disable EmptyGeneralCatchClause
            catch (Exception)
            // ReSharper restore EmptyGeneralCatchClause
            {
            }
        }

        private void RefreshConnectionStatus()
        {
            MySqlConnection.TestConnect();

            if (MySqlConnection.Connected)
            {
                _dbConnect.Text = @"Connection successful.";
                _dbConnect.ForeColor = Color.Green;
            }
            else
            {
                _dbConnect.Text = @"No DB Connected";
                _dbConnect.ForeColor = Color.Red;
            }
        }

        private void TextBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!((char.IsDigit(e.KeyChar) || e.KeyChar == (char)Keys.Back)))
                e.Handled = true;
        }

        private void LevelScalingClick(object sender, EventArgs e)
        {
            var scalingForm = new FormSpellScaling();
            var ret = scalingForm.ShowDialog(this);
            if (ret == DialogResult.OK)
            {
                DBC.DBC.SelectedLevel = scalingForm.SelectedLevel;
                DBC.DBC.SelectedItemLevel = scalingForm.SelectedItemLevel;
                DBC.DBC.SelectedMapDifficulty = scalingForm.SelectedMapDifficulty;
                switch (tabControl1.SelectedIndex)
                {
                    case 0:
                        break;
                    case 1:
                        LvSpellListSelectedIndexChanged(null, null);
                        break;
                    case 2:
                        LvProcSpellListSelectedIndexChanged(null, null);
                        break;
                    case 3:
                        break;
                    case 4:
                        CompareFilterSpellTextChanged(null, null);
                        break;
                }
            }
        }

        #endregion

        #region SPELLHELL TRACKER PAGE

        private void OnClassSpecHeroSelected(string className, string specName, string heroName)
        {
            if (_talentTreeControl == null) return;

            _currentClassName = className;
            _currentSpecName  = specName;
            _currentHeroName  = heroName;

            // If the DB already has data for this combination, load it directly.
            if (Database.MySqlConnection.Connected &&
                Database.SpellHellTrackerDb.HasData(className, specName, heroName))
            {
                var dbTrees = Database.SpellHellTrackerDb.TryLoadTrees(className, specName, heroName);
                if (dbTrees != null)
                {
                    _talentTreeControl.SetTrees(dbTrees);
                    if (_lblTrackerStatus != null)
                        _lblTrackerStatus.Text =
                            $"Loaded from DB – {className} / {specName} / {heroName}  |  " +
                            Database.SpellHellTrackerDb.LastIconDiagnostic;
                    return;
                }
            }

            // No data available – show empty canvas.
            _talentTreeControl.SetTrees(Array.Empty<TalentTree>());
            if (_lblTrackerStatus != null)
                _lblTrackerStatus.Text =
                    $"No data for {className} / {specName} / {heroName}. " +
                    "Import the SQL files first, then re-select.";
        }

        private void OnNodeOpenInSpellInfo(TalentNode node)
        {
            if (node.SpellId <= 0) return;
            tabControl1.SelectedTab = _tpSpellInfo;
            _tbSearchId.Text = node.SpellId.ToString();
            AdvancedSearch();
        }

        private void OnNodeStatusNotesUpdated(TalentNode node, string status, string notes)
        {
            if (!Database.MySqlConnection.Connected) return;
            Database.SpellHellTrackerDb.UpdateSpellStatusNotes(node.ActiveSpellId, status, notes);
            if (_lblTrackerStatus != null)
                _lblTrackerStatus.Text =
                    $"{node.ActiveName} (ID {node.ActiveSpellId}): status={status}" +
                    (string.IsNullOrEmpty(notes) ? string.Empty : $", notes updated");
        }

        private async void _unused1(object sender, EventArgs e)
        {
            try
            {
                var parsed = await WowheadTalentParser.FetchAndParseAsync(
                    _currentClassName, _currentSpecName, _currentHeroName);

                _talentTreeControl.SetTrees(parsed.Trees);

                var trees    = parsed.Trees;
                var nodeSummary =
                    $"({trees[0].Nodes.Count} + {trees[1].Nodes.Count} + {trees[2].Nodes.Count} nodes)";

                // ── Database sync ──────────────────────────────────────────
                string dbStatus = string.Empty;
                if (Database.MySqlConnection.Connected)
                {
                    if (_lblWowheadStatus != null)
                        _lblWowheadStatus.Text = "Syncing with spellhell_tracker DB…";

                    if (!Database.SpellHellTrackerDb.EnsureSchema())
                    {
                        dbStatus = "  [DB schema init failed]";
                    }
                    else
                    {
                        // Upsert spells (preserves user-written status/notes on re-sync).
                        Database.SpellHellTrackerDb.UpsertSpells(
                            parsed.Spells.Select(s =>
                                (s.SpellId, s.SpellName,
                                 _currentClassName, _currentSpecName, _currentHeroName,
                                 s.TreeType)));

                        // Diff against what is stored and generate SQL patch if needed.
                        var syncResult = Database.SpellHellTrackerDb.DiffAndApply(
                            trees,
                            _currentClassName, _currentSpecName, _currentHeroName,
                            AppContext.BaseDirectory);

                        dbStatus = syncResult.HasChanges
                            ? $"  SQL patch: {Path.GetFileName(syncResult.SqlFilePath)}" +
                              $" (+{syncResult.AddedNodes}/~{syncResult.UpdatedNodes}/-{syncResult.RemovedNodes} nodes," +
                              $" +{syncResult.AddedConns}/-{syncResult.RemovedConns} conns)"
                            : "  DB up-to-date (no changes)";
                    }
                }
                else
                {
                    dbStatus = "  (no DB connection – skipped)";
                }

                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text =
                        $"Synced – {_currentClassName} / {_currentSpecName} / {_currentHeroName} " +
                        nodeSummary + dbStatus;
            }
            catch (Exception ex)
            {
                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text = $"Error: {ex.Message}";

                MessageBox.Show(
                    $"Failed to sync talent trees from Wowhead:\n{ex.Message}",
                    "Wowhead Sync",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
            }
        }

        private async void ImportSqlClick(object sender, EventArgs e)
        {
            try
            {
                if (!Database.MySqlConnection.Connected)
                {
                    MessageBox.Show("No database connection. Configure the connection in Settings.",
                        "Import SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sqlDir = Path.Combine(AppContext.BaseDirectory, "SQL");
                if (!Directory.Exists(sqlDir))
                {
                    MessageBox.Show("SQL folder not found. Click \"Generate All SQL\" first.",
                        "Import SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (executed, errors) = await Task.Run(() =>
                    Database.SpellHellTrackerDb.ImportSqlFiles(sqlDir));

                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text =
                        $"Import done – {executed} statements executed, {errors} errors. " +
                        "Select a class to view the talent tree.";

                // Reload the current selection from the freshly imported data.
                if (!string.IsNullOrEmpty(_currentClassName))
                    OnClassSpecHeroSelected(_currentClassName, _currentSpecName, _currentHeroName);
            }
            catch (Exception ex)
            {
                if (_lblWowheadStatus != null) _lblWowheadStatus.Text = $"Import error: {ex.Message}";
                MessageBox.Show($"Import failed:\n{ex.Message}", "Import SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_bImportSql != null) _bImportSql.Enabled = true;
            }
        }

        private async void GenerateSqlClick(object sender, EventArgs e)
        {
            try
            {
                var outputDir  = AppContext.BaseDirectory;
                var allClasses = TalentTreeControl.AllClasses;
                int total      = allClasses.Sum(c => c.Specs.Sum(s => s.HeroTalents.Length));

                // Always create the SQL folder and write the schema bootstrap file
                // so the folder is visible even before any class data is parsed.
                var schemaFile = Database.SpellHellTrackerDb.GenerateSchemaFile(outputDir);

                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text = $"Schema written → {Path.GetFileName(schemaFile)}. Fetching talent bundle…";

                // Fetch the data bundle once – it contains every class/spec/hero combination.
                var seedUrl  = WowheadTalentParser.BuildUrl("warrior", "arms", "colossus");
                var pageData = await WowheadTalentParser.FetchPageDataAsync(seedUrl);

                // Dump the raw bundle so we can diagnose parser issues if needed.
                WowheadTalentParser.DumpBundleStructure(pageData, outputDir);

                // Ensure schema exists in DB so DiffAndApply can compare; skip if not connected.
                if (Database.MySqlConnection.Connected)
                    Database.SpellHellTrackerDb.EnsureSchema();

                int filesWritten  = 0;
                int emptyTrees    = 0;
                int parseErrors   = 0;

                await Task.Run(() =>
                {
                    int done = 0;
                    foreach (var cls in allClasses)
                    {
                        foreach (var spec in cls.Specs)
                        {
                            foreach (var hero in spec.HeroTalents)
                            {
                                done++;
                                try
                                {
                                    var parsed = WowheadTalentParser.BuildResultFromPageData(
                                        pageData, cls.Name, spec.Name, hero);

                                    int nodeCount = parsed.Trees.Sum(t => t.Nodes.Count);
                                    if (nodeCount == 0)
                                    {
                                        emptyTrees++;
                                    }
                                    else
                                    {
                                        var syncResult = Database.SpellHellTrackerDb.DiffAndApply(
                                            parsed.Trees, cls.Name, spec.Name, hero, outputDir);
                                        if (syncResult.HasChanges)
                                            filesWritten++;
                                    }
                                }
                                catch
                                {
                                    parseErrors++;
                                }
                            }
                        }
                    }
                });

                var summary = new System.Text.StringBuilder();
                summary.Append($"Done – {filesWritten}/{total} SQL files written to SQL\\ folder");
                if (emptyTrees > 0)
                    summary.Append($", {emptyTrees} empty (parser returned no nodes)");
                if (parseErrors > 0)
                    summary.Append($", {parseErrors} errors");
                summary.Append('.');

                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text = summary.ToString();

                if (emptyTrees > 0 || parseErrors > 0)
                    MessageBox.Show(
                        summary.ToString() + "\n\n" +
                        "Empty results usually mean the Wowhead bundle did not contain matching\n" +
                        "class/spec/hero tree data. Try syncing a single class first to confirm\n" +
                        "the parser is working.",
                        "Generate All SQL",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                if (_lblWowheadStatus != null)
                    _lblWowheadStatus.Text = $"Error: {ex.Message}";

                MessageBox.Show(
                    $"Failed to generate SQL files:\n{ex.Message}",
                    "Generate All SQL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (_bGenerateSql != null) _bGenerateSql.Enabled = true;
                if (_bImportSql   != null) _bImportSql.Enabled   =
                    Directory.Exists(Path.Combine(AppContext.BaseDirectory, "SQL"));
            }
        }

        private static TalentTree RenameTree(TalentTree source, string newName) =>
            new TalentTree { Name = newName, Nodes = source.Nodes };

        /// <summary>
        /// Returns spells from the DBC and sniff parser lists that are related to the given
        /// talent spell ID.  Includes directly triggered spells, required aura chains,
        /// same-name sniff captures, OriginalCastID-linked triggers, description name matches,
        /// modifier-aura hooked spells (when named in description), and consumer chains
        /// (spells that consume an already-included buff, e.g. Arcane Missiles → Clearcasting).
        /// </summary>
        private IReadOnlyList<SpellInfo> GetImplementationSpells(int talentSpellId)
        {
            if (!DBC.DBC.SpellInfoStore.TryGetValue(talentSpellId, out var talentSpell))
                return Array.Empty<SpellInfo>();

            var talentName = talentSpell.Name;
            var result     = new List<SpellInfo>();
            var seenIds    = new HashSet<int> { talentSpellId };

            // Only adds to seenIds (and result) when the spell has not been seen before.
            // IMPORTANT: seenIds.Add must be the LAST condition so it only fires when all
            // prior checks pass — avoiding the bug where iterating the full list consumed
            // every ID into seenIds before the description-text passes could see them.
            bool TryInclude(SpellInfo s)
            {
                if (seenIds.Contains(s.ID)) return false;
                seenIds.Add(s.ID);
                result.Add(s);
                return true;
            }

            // Includes a spell by ID and also follows its EffectTriggerSpell chain one level
            // deep (e.g. CasterAuraSpell → the aura itself → what it fires when consumed).
            void IncludeWithTriggerChain(int spellId)
            {
                if (spellId == 0 || !DBC.DBC.SpellInfoStore.TryGetValue(spellId, out var s)) return;
                if (!TryInclude(s)) return;
                foreach (var eff in s.SpellEffectInfoStore)
                    if (eff.EffectTriggerSpell != 0 &&
                        DBC.DBC.SpellInfoStore.TryGetValue(eff.EffectTriggerSpell, out var trig))
                        TryInclude(trig);
            }

            // DBC Pass 0: spells directly triggered by the talent's own effects.
            foreach (var eff in talentSpell.SpellEffectInfoStore)
                if (eff.EffectTriggerSpell != 0)
                    IncludeWithTriggerChain(eff.EffectTriggerSpell);

            // DBC Pass 1: CasterAuraSpell / TargetAuraSpell – the aura the talent
            // requires or interacts with, plus any spells those auras themselves trigger.
            IncludeWithTriggerChain(talentSpell.CasterAuraSpell);
            IncludeWithTriggerChain(talentSpell.TargetAuraSpell);

            // Pass 2: spells in the casted list with the same name as the talent
            foreach (var s in _spellHellParserCastedList)
                if (s.Name.Equals(talentName, StringComparison.OrdinalIgnoreCase))
                    TryInclude(s);

            // Pass 3: spells in the known-spell list with the same name as the talent
            foreach (var s in _spellHellParserList)
                if (s.Name.Equals(talentName, StringComparison.OrdinalIgnoreCase))
                    TryInclude(s);

            // Pass 4: spells that were observed being triggered by this talent (OriginalCastID)
            foreach (var kvp in _spellHellParserCastedByMap)
                if (kvp.Value == talentSpellId &&
                    DBC.DBC.SpellInfoStore.TryGetValue(kvp.Key, out var triggeredSpell))
                    TryInclude(triggeredSpell);

            // Pass 5: spell names literally mentioned in the talent's Description / Tooltip
            // — search sniff lists first, then fall back to DBC same-family lookup.
            var descText = string.Concat(talentSpell.Description, " ", talentSpell.Tooltip);
            if (!string.IsNullOrWhiteSpace(descText))
            {
                foreach (var s in _spellHellParserCastedList)
                    if (!string.IsNullOrEmpty(s.Name) && s.Name.Length >= 4 &&
                        descText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                        TryInclude(s);

                foreach (var s in _spellHellParserList)
                    if (!string.IsNullOrEmpty(s.Name) && s.Name.Length >= 4 &&
                        descText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                        TryInclude(s);

                // DBC fallback: same-family spells whose name appears in the description.
                // Guard: skip generic class passives (SpellClassMask all-zero – these are
                // marker spells like "Mage" / "Warrior" with no actual affected spells).
                foreach (var s in DBC.DBC.SpellInfoStore.Values)
                    if (s.SpellFamilyName == talentSpell.SpellFamilyName &&
                        !string.IsNullOrEmpty(s.Name) && s.Name.Length >= 4 &&
                        s.SpellClassMask.Any(m => m != 0) &&
                        descText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                        TryInclude(s);
            }

            // DBC Pass 6: modifier-aura talents (ADD_*_MODIFIER) – find the spell being
            // hooked via the ClassMask, but only when its name also appears in the
            // description to avoid pulling in every spell with a matching family bit.
            if (!string.IsNullOrWhiteSpace(descText))
            {
                var modifierAuras = new HashSet<AuraType>
                {
                    AuraType.SPELL_AURA_ADD_FLAT_MODIFIER,
                    AuraType.SPELL_AURA_ADD_PCT_MODIFIER,
                    AuraType.SPELL_AURA_ADD_PCT_MODIFIER_BY_SPELL_LABEL,
                    AuraType.SPELL_AURA_ADD_FLAT_MODIFIER_BY_SPELL_LABEL,
                };
                foreach (var eff in talentSpell.SpellEffectInfoStore)
                {
                    if (!modifierAuras.Contains((AuraType)eff.EffectAura)) continue;
                    var classMask = Array.ConvertAll(eff.SpellEffect.EffectSpellClassMask, m => (uint)m);
                    if (classMask.All(m => m == 0)) continue;

                    foreach (var s in DBC.DBC.SpellInfoStore.Values)
                    {
                        if (s.SpellFamilyName != talentSpell.SpellFamilyName) continue;
                        if (!s.SpellClassMask.ContainsElement(classMask)) continue;
                        if (string.IsNullOrEmpty(s.Name) || s.Name.Length < 4) continue;
                        if (!descText.Contains(s.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        TryInclude(s);
                    }
                }
            }

            // DBC Pass 7: "consumer chain" – for each buff/aura already included (has a
            // non-zero duration), find same-family spells whose CasterAuraSpell points to
            // it (i.e. spells that require/consume that buff) and include them with their
            // own trigger chain.  Example: Clearcasting (included via description) →
            // Arcane Missiles (CasterAuraSpell == Clearcasting) → its damage trigger.
            var buffSnapshot = result.ToList();
            foreach (var buffSpell in buffSnapshot)
            {
                if (buffSpell.DurationEntry == null || buffSpell.DurationEntry.Duration == 0) continue;
                foreach (var s in DBC.DBC.SpellInfoStore.Values)
                {
                    if (s.SpellFamilyName != talentSpell.SpellFamilyName) continue;
                    if (s.CasterAuraSpell != buffSpell.ID) continue;
                    IncludeWithTriggerChain(s.ID);
                }
            }

            // Sniff transitive pass: for each spell in result that has SPELL_EFFECT_SUMMON,
            // include any sniff-casted spell whose casted-by map entry points to it.
            // Restricted to summon-origin links so that regular OriginalCastID-triggered
            // spells (e.g. Arcane Surge triggering other Mage spells) are NOT pulled in.
            var resultIdSnapshot = new HashSet<int>(result.Select(s => s.ID));
            foreach (var kvp in _spellHellParserCastedByMap)
            {
                if (!resultIdSnapshot.Contains(kvp.Value)) continue;
                if (!DBC.DBC.SpellInfoStore.TryGetValue(kvp.Value, out var originSpell)) continue;

                // Only follow the chain when the origin spell summoned a creature.
                bool originHasSummon = originSpell.SpellEffectInfoStore
                    .Any(eff => eff.Effect == (int)SpellEffects.SPELL_EFFECT_SUMMON);
                if (!originHasSummon) continue;

                if (DBC.DBC.SpellInfoStore.TryGetValue(kvp.Key, out var transitiveSpell))
                    TryInclude(transitiveSpell);
            }

            return result;
        }

        // Builds a WoW talent-tree-style placeholder with 3 trees: Class, Hero, Spec.
        // Each tree is generated from a (col, row, isGate, maxRank) layout; nodes are
        // auto-connected to any neighbour in the row below (diag-left, straight, diag-right).
        private static IReadOnlyList<TalentTree> BuildPlaceholderTalentTrees()
        {
            return new[]
            {
                BuildTree("Class",  1,   BuildClassLayout()),
                BuildTree("Hero",   200, BuildHeroLayout()),
                BuildTree("Spec",   400, BuildSpecLayout()),
            };
        }

        private static TalentTree BuildTree(string name, int baseId,
            IEnumerable<(int col, int row, bool gate, int max)> layout)
        {
            var nodes = new List<TalentNode>();
            var byPos = new Dictionary<(float, int), TalentNode>();
            int id = baseId;

            foreach (var (col, row, gate, max) in layout)
            {
                var n = new TalentNode { Id = id++, Col = col, Row = row, IsGate = gate, MaxRank = max };
                nodes.Add(n);
                byPos[((float)col, row)] = n;
            }

            // Auto-connect every node to neighbours one row below it (diag-left, straight, diag-right)
            foreach (var node in nodes)
                for (int dc = -1; dc <= 1; dc++)
                    if (byPos.TryGetValue((node.Col + dc, node.Row + 1), out var child)
                        && !node.ChildIds.Contains(child.Id))
                        node.ChildIds.Add(child.Id);

            return new TalentTree { Name = name, Nodes = nodes };
        }

        // Class tree  (6 columns, 10 rows – Warrior-like diamond layout)
        private static IEnumerable<(int, int, bool, int)> BuildClassLayout() =>
        [
            (1, 0, true,  1), (4, 0, true,  1),                            // row 0 gates
            (0, 1, false, 1), (2, 1, false, 1), (3, 1, false, 1), (5, 1, false, 1),
            (0, 2, false, 1), (1, 2, false, 2), (2, 2, false, 1), (3, 2, false, 1), (4, 2, false, 2), (5, 2, false, 1),
            (0, 3, false, 1), (1, 3, false, 1), (2, 3, false, 1), (3, 3, false, 1), (4, 3, false, 1), (5, 3, false, 1),
            (0, 4, false, 2), (1, 4, false, 1), (2, 4, false, 1), (3, 4, false, 1), (4, 4, false, 1), (5, 4, false, 2),
            (1, 5, false, 1), (2, 5, false, 1), (3, 5, false, 2), (4, 5, false, 1),
            (0, 6, false, 1), (2, 6, false, 1), (3, 6, false, 1), (5, 6, false, 1),
            (1, 7, false, 1), (2, 7, false, 2), (3, 7, false, 1), (4, 7, false, 1),
            (1, 8, false, 1), (3, 8, false, 1), (4, 8, false, 2),
            (2, 9, false, 1), (3, 9, false, 4),
        ];

        // Hero tree  (4 columns, 6 rows – narrower centre tree)
        private static IEnumerable<(int, int, bool, int)> BuildHeroLayout() =>
        [
            (1, 0, true,  1), (2, 0, true,  1),                            // row 0 gates
            (0, 1, false, 1), (1, 1, false, 1), (2, 1, false, 1), (3, 1, false, 1),
            (0, 2, false, 1), (1, 2, false, 1), (2, 2, false, 1), (3, 2, false, 1),
            (1, 3, false, 1), (2, 3, false, 1),
            (0, 4, false, 1), (1, 4, false, 2), (2, 4, false, 1), (3, 4, false, 1),
            (1, 5, false, 1), (2, 5, false, 1),
        ];

        // Spec tree  (6 columns, 10 rows – mirror of class tree)
        private static IEnumerable<(int, int, bool, int)> BuildSpecLayout() =>
        [
            (0, 0, true,  1), (3, 0, true,  1),                            // row 0 gates
            (0, 1, false, 1), (1, 1, false, 1), (2, 1, false, 1), (4, 1, false, 1), (5, 1, false, 1),
            (0, 2, false, 2), (1, 2, false, 1), (2, 2, false, 1), (3, 2, false, 1), (4, 2, false, 2), (5, 2, false, 1),
            (0, 3, false, 1), (1, 3, false, 1), (2, 3, false, 1), (3, 3, false, 1), (4, 3, false, 1), (5, 3, false, 1),
            (0, 4, false, 1), (1, 4, false, 2), (2, 4, false, 1), (3, 4, false, 1), (4, 4, false, 1), (5, 4, false, 2),
            (1, 5, false, 1), (2, 5, false, 2), (3, 5, false, 1), (4, 5, false, 1),
            (0, 6, false, 1), (1, 6, false, 1), (3, 6, false, 1), (5, 6, false, 1),
            (1, 7, false, 1), (2, 7, false, 1), (3, 7, false, 2), (4, 7, false, 1),
            (2, 8, false, 2), (3, 8, false, 1), (4, 8, false, 1),
            (2, 9, false, 4), (4, 9, false, 1),
        ];

        #endregion

        #region SPELLHELL PARSER PAGE

        private static readonly HashSet<uint> _playerClassFamilies = new HashSet<uint>
        {
            (uint)SpellFamilyNames.SPELLFAMILY_WARRIOR,
            (uint)SpellFamilyNames.SPELLFAMILY_MAGE,
            (uint)SpellFamilyNames.SPELLFAMILY_WARLOCK,
            (uint)SpellFamilyNames.SPELLFAMILY_PRIEST,
            (uint)SpellFamilyNames.SPELLFAMILY_DRUID,
            (uint)SpellFamilyNames.SPELLFAMILY_ROGUE,
            (uint)SpellFamilyNames.SPELLFAMILY_HUNTER,
            (uint)SpellFamilyNames.SPELLFAMILY_PALADIN,
            (uint)SpellFamilyNames.SPELLFAMILY_SHAMAN,
            (uint)SpellFamilyNames.SPELLFAMILY_DEATHKNIGHT,
            (uint)SpellFamilyNames.SPELLFAMILY_MONK,
            (uint)SpellFamilyNames.SPELLFAMILY_DEMON_HUNTER,
            (uint)SpellFamilyNames.SPELLFAMILY_EVOKER,
        };

        private const string SpellHellParserSignature = "# TrinityCore - WowPacketParser";

        private List<SpellInfo> _spellHellParserList       = new List<SpellInfo>();
        private List<SpellInfo> _spellHellParserCastedList  = new List<SpellInfo>();
        // Maps a casted spell ID to the ID of the class spell that originally triggered it (OriginalCastID)
        private Dictionary<int, int> _spellHellParserCastedByMap = new Dictionary<int, int>();

        // Raw sniff observations: NPC entry → set of spell IDs the summoned creature was seen casting.
        // Populated regardless of whether the spell ID exists in DBC so that unknown spells (e.g.
        // "-Unknown-" entries) are still surfaced in the IMPLEMENT prompt.
        private Dictionary<int, HashSet<int>> _spellHellParserSummonedCasts = new Dictionary<int, HashSet<int>>();

        // Filtered views (same reference as raw list when filter = "All Classes")
        private List<SpellInfo> _spellHellParserListView       = new List<SpellInfo>();
        private List<SpellInfo> _spellHellParserCastedListView = new List<SpellInfo>();

        // Programmatically-created class filter control
        private ComboBox? _cbSpellHellParserClassFilter;

        // Ordered class → SpellFamilyNames mapping used to populate the filter ComboBox.
        // Alphabetical order; must stay in sync with the ComboBox item indices (index 0 = "All").
        private static readonly (string DisplayName, SpellFamilyNames Family)[] s_parserClassFilters =
        [
            ("Death Knight",  SpellFamilyNames.SPELLFAMILY_DEATHKNIGHT),
            ("Demon Hunter",  SpellFamilyNames.SPELLFAMILY_DEMON_HUNTER),
            ("Druid",         SpellFamilyNames.SPELLFAMILY_DRUID),
            ("Evoker",        SpellFamilyNames.SPELLFAMILY_EVOKER),
            ("Hunter",        SpellFamilyNames.SPELLFAMILY_HUNTER),
            ("Mage",          SpellFamilyNames.SPELLFAMILY_MAGE),
            ("Monk",          SpellFamilyNames.SPELLFAMILY_MONK),
            ("Paladin",       SpellFamilyNames.SPELLFAMILY_PALADIN),
            ("Priest",        SpellFamilyNames.SPELLFAMILY_PRIEST),
            ("Rogue",         SpellFamilyNames.SPELLFAMILY_ROGUE),
            ("Shaman",        SpellFamilyNames.SPELLFAMILY_SHAMAN),
            ("Warlock",       SpellFamilyNames.SPELLFAMILY_WARLOCK),
            ("Warrior",       SpellFamilyNames.SPELLFAMILY_WARRIOR),
        ];

        private void SpellHellParserBrowseClick(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = @"Text files|*.txt",
                Title = @"Select file to parse"
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var path = ofd.FileName;

            if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(@"Not a valid file. Please select a valid file to parse.", @"Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _tbSpellHellParserInput.Text = string.Empty;
                _bSpellHellParserParse.Enabled = false;
                return;
            }

            string firstLine;
            try
            {
                firstLine = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                MessageBox.Show(@"Not a valid file. Please select a valid file to parse.", @"Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _tbSpellHellParserInput.Text = string.Empty;
                _bSpellHellParserParse.Enabled = false;
                return;
            }

            if (!firstLine.StartsWith(SpellHellParserSignature, StringComparison.Ordinal))
            {
                MessageBox.Show(@"Not a valid file. Please select a valid file to parse.", @"Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _tbSpellHellParserInput.Text = string.Empty;
                _bSpellHellParserParse.Enabled = false;
                return;
            }

            _tbSpellHellParserInput.Text = path;
            _bSpellHellParserParse.Enabled = true;
        }

        private void SpellHellParserParseClick(object sender, EventArgs e)
        {
            SpellHellParserExecute();
        }

        private void SpellHellParserExecute()
        {
            var path = _tbSpellHellParserInput.Text;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            _rtbSpellHellParser.Clear();
            _spellHellParserList.Clear();
            _spellHellParserCastedList.Clear();
            _spellHellParserCastedByMap.Clear();
            _spellHellParserSummonedCasts.Clear();

            var seenIds = new HashSet<int>();
            var seenCastedIds = new HashSet<int>();
            // Track whether we are inside a cast packet block and its originating class spell
            bool inCastBlock = false;
            bool inAuraUpdateBlock = false;
            int currentBlockOriginalSpellId = 0;
            foreach (var line in File.ReadLines(path))
            {
                // Detect entry into a cast packet block
                if (line.Contains("SMSG_SPELL_START") || line.Contains("SMSG_SPELL_GO") || line.Contains("SMSG_SPELL_PREPARE"))
                {
                    inCastBlock = true;
                    inAuraUpdateBlock = false;
                    currentBlockOriginalSpellId = 0;
                }
                else if (line.Contains("SMSG_AURA_UPDATE"))
                {
                    inAuraUpdateBlock = true;
                    inCastBlock = false;
                    currentBlockOriginalSpellId = 0;
                }
                else if (line.Length > 0 && line.StartsWith("ServerToClient:") || line.StartsWith("ClientToServer:"))
                {
                    // A new packet header line ends the current block
                    inCastBlock = false;
                    inAuraUpdateBlock = false;
                    currentBlockOriginalSpellId = 0;
                }

                // Track OriginalCastID Entry inside cast blocks to allow generic spells triggered by class spells
                if (inCastBlock && currentBlockOriginalSpellId == 0)
                {
                    var origMatch = _originalCastEntryRegex.Match(line);
                    if (origMatch.Success && int.TryParse(origMatch.Groups[1].Value, out var origId)
                        && DBC.DBC.SpellInfoStore.TryGetValue(origId, out var origSpell)
                        && _playerClassFamilies.Contains(origSpell.SpellFamilyName))
                    {
                        currentBlockOriginalSpellId = origId;
                    }
                }

                // --- Known spells ---
                var knownIdx = line.IndexOf("KnownSpellId", StringComparison.Ordinal);
                if (knownIdx >= 0)
                {
                    var rest = line.Substring(knownIdx + "KnownSpellId".Length).TrimStart(':', '=', ' ', '\t');
                    var spaceIdx = rest.IndexOfAny(new[] { ' ', '\t', ',', ';', '\r', '\n' });
                    var token = spaceIdx >= 0 ? rest.Substring(0, spaceIdx) : rest;
                    if (int.TryParse(token, out var knownId) && !seenIds.Contains(knownId)
                        && DBC.DBC.SpellInfoStore.TryGetValue(knownId, out var knownSpell)
                        && _playerClassFamilies.Contains(knownSpell.SpellFamilyName))
                    {
                        seenIds.Add(knownId);
                        _spellHellParserList.Add(knownSpell);
                    }
                    continue;
                }

                // --- Aura update spells ---
                if (inAuraUpdateBlock)
                {
                    var am = _auraUpdateSpellLineRegex.Match(line);
                    if (am.Success && int.TryParse(am.Groups[1].Value, out var auraSpellId)
                        && !seenCastedIds.Contains(auraSpellId)
                        && DBC.DBC.SpellInfoStore.TryGetValue(auraSpellId, out var auraSpell)
                        && _playerClassFamilies.Contains(auraSpell.SpellFamilyName))
                    {
                        seenCastedIds.Add(auraSpellId);
                        _spellHellParserCastedList.Add(auraSpell);
                    }
                    continue;
                }

                // --- Casted spells ---
                if (!inCastBlock)
                    continue;

                var cm = _castedSpellLineRegex.Match(line);
                if (!cm.Success)
                    continue;

                if (!int.TryParse(cm.Groups[1].Value, out var castedId) || seenCastedIds.Contains(castedId))
                    continue;

                if (!DBC.DBC.SpellInfoStore.TryGetValue(castedId, out var castedSpell))
                    continue;

                if (!_playerClassFamilies.Contains(castedSpell.SpellFamilyName)
                    && currentBlockOriginalSpellId == 0)
                    continue;

                seenCastedIds.Add(castedId);
                _spellHellParserCastedList.Add(castedSpell);
                if (currentBlockOriginalSpellId != 0 && currentBlockOriginalSpellId != castedId)
                    _spellHellParserCastedByMap[castedId] = currentBlockOriginalSpellId;
            }

            _spellHellParserList.Sort(SpellInfo.Comparer.Instance);
            _spellHellParserCastedList.Sort(SpellInfo.Comparer.Instance);

            // ── Phase 2: collect spells cast by summoned units ───────────────────────────
            // For every spell already collected, scan its DBC SPELL_EFFECT_SUMMON effects
            // to build a map: creature NPC entry ID → originating class spell ID.
            var summonCreatureToSpell = new Dictionary<int, int>();
            foreach (var spell in _spellHellParserList.Concat(_spellHellParserCastedList))
                foreach (var eff in spell.SpellEffectInfoStore)
                    if (eff.Effect == (int)SpellEffects.SPELL_EFFECT_SUMMON && eff.EffectMiscValueA != 0)
                        summonCreatureToSpell.TryAdd(eff.EffectMiscValueA, spell.ID);

            if (summonCreatureToSpell.Count > 0)
            {
                bool inSummonBlock     = false;
                int  summonCreatureId  = 0;
                int  summonCastSpellId = 0;

                // Commits the current summon-cast block if both a tracked creature and a
                // spell ID have been identified; resets tracking state afterwards.
                void CommitSummonCast()
                {
                    if (summonCreatureId == 0 || summonCastSpellId == 0) return;

                    // Always record the raw observation so the IMPLEMENT prompt can surface
                    // spells that are not (yet) in DBC (e.g. "-Unknown-" creature-cast spells).
                    if (!_spellHellParserSummonedCasts.TryGetValue(summonCreatureId, out var castSet))
                        _spellHellParserSummonedCasts[summonCreatureId] = castSet = new HashSet<int>();
                    castSet.Add(summonCastSpellId);

                    if (!seenCastedIds.Contains(summonCastSpellId) &&
                        DBC.DBC.SpellInfoStore.TryGetValue(summonCastSpellId, out var sc))
                    {
                        seenCastedIds.Add(summonCastSpellId);
                        _spellHellParserCastedList.Add(sc);
                        if (summonCreatureToSpell.TryGetValue(summonCreatureId, out var originId))
                            _spellHellParserCastedByMap[summonCastSpellId] = originId;
                    }
                    summonCreatureId  = 0;
                    summonCastSpellId = 0;
                }

                foreach (var line2 in File.ReadLines(path))
                {
                    if (line2.Contains("SMSG_SPELL_START") || line2.Contains("SMSG_SPELL_GO") ||
                        line2.Contains("SMSG_SPELL_PREPARE"))
                    {
                        CommitSummonCast();
                        inSummonBlock     = true;
                        summonCreatureId  = 0;
                        summonCastSpellId = 0;
                    }
                    else if (line2.Length > 0 &&
                             (line2.StartsWith("ServerToClient:") || line2.StartsWith("ClientToServer:")))
                    {
                        CommitSummonCast();
                        inSummonBlock = false;
                    }

                    if (!inSummonBlock) continue;

                    // Detect that the caster is one of the tracked summoned creatures.
                    if (summonCreatureId == 0)
                    {
                        var cmg = _casterGuidEntryRegex.Match(line2);
                        if (cmg.Success && int.TryParse(cmg.Groups[1].Value, out var creId) &&
                            summonCreatureToSpell.ContainsKey(creId))
                            summonCreatureId = creId;
                    }

                    // Detect the spell being cast.
                    // Primary source: CastID GUID Entry (the Entry of a Cast-type GUID IS the spell ID).
                    // Fallback: explicit "(Cast) SpellID:" line.
                    // We deliberately avoid the bare "Entry:" branch of _castedSpellLineRegex
                    // here because that also appears on the CasterGUID line.
                    if (summonCastSpellId == 0)
                    {
                        var cid = _castIdEntryRegex.Match(line2);
                        if (cid.Success && int.TryParse(cid.Groups[1].Value, out var castIdEntry))
                            summonCastSpellId = castIdEntry;
                    }
                    if (summonCastSpellId == 0)
                    {
                        var cms = _castedSpellLineRegex.Match(line2);
                        if (cms.Success && cms.Groups[1].Success &&
                            int.TryParse(cms.Groups[1].Value, out var castId))
                            summonCastSpellId = castId;
                    }
                }
                CommitSummonCast(); // flush the last block in the file

                _spellHellParserCastedList.Sort(SpellInfo.Comparer.Instance);
            }

            // Rebuild the filtered views and refresh both ListViews.
            // ApplyParserClassFilter also pushes the visible IDs to the talent tracker.
            ApplyParserClassFilter();
        }

        private void SpellHellParserClassFilterChanged(object? sender, EventArgs e)
        {
            ApplyParserClassFilter();
        }

        /// <summary>
        /// Rebuilds the view lists from the raw parser lists, applying the current class filter,
        /// then refreshes both ListViews.
        /// </summary>
        private void ApplyParserClassFilter()
        {
            uint? family = null;
            if (_cbSpellHellParserClassFilter != null &&
                _cbSpellHellParserClassFilter.SelectedIndex > 0)
            {
                family = (uint)s_parserClassFilters[
                    _cbSpellHellParserClassFilter.SelectedIndex - 1].Family;
            }

            // Known spells: strict family match when a filter is active.
            _spellHellParserListView = family.HasValue
                ? _spellHellParserList.Where(s => s.SpellFamilyName == family.Value).ToList()
                : _spellHellParserList;

            // Casted spells: keep the selected class AND any spell that does NOT belong to
            // another player-class family (generic procs, triggered effects, etc. are always
            // kept so they can still be used as reference context).
            _spellHellParserCastedListView = family.HasValue
                ? _spellHellParserCastedList
                      .Where(s => s.SpellFamilyName == family.Value ||
                                  !_playerClassFamilies.Contains(s.SpellFamilyName))
                      .ToList()
                : _spellHellParserCastedList;

            _lvSpellHellParserList.VirtualListSize = _spellHellParserListView.Count;
            if (_lvSpellHellParserList.SelectedIndices.Count > 0)
                _lvSpellHellParserList.Items[_lvSpellHellParserList.SelectedIndices[0]].Selected = false;
            _lvSpellHellParserList.Invalidate();

            _lvSpellHellParserCastedList.VirtualListSize = _spellHellParserCastedListView.Count;
            if (_lvSpellHellParserCastedList.SelectedIndices.Count > 0)
                _lvSpellHellParserCastedList.Items[_lvSpellHellParserCastedList.SelectedIndices[0]].Selected = false;
            _lvSpellHellParserCastedList.Invalidate();

            // Sync the talent-tracker blue rings to match exactly what is visible in both lists.
            if (_talentTreeControl != null)
            {
                var visibleIds = _spellHellParserListView.Select(s => s.ID)
                    .Concat(_spellHellParserCastedListView.Select(s => s.ID));
                _talentTreeControl.SetSniffMatchedSpellIds(visibleIds);
            }
        }

        private void LvSpellHellParserListSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvSpellHellParserList.SelectedIndices.Count > 0)
            {
                var spell = _spellHellParserListView[_lvSpellHellParserList.SelectedIndices[0]];
                spell.Write(_rtbSpellHellParser);
                WriteSpellHellParserRefs(spell);
            }
        }

        // Matches all documented spell description variable forms.
        // Group 1 = explicit spell ID prefix  (e.g. $1227162s1  -> "1227162")
        // Group 2 = variable letter when explicit ID present     (e.g. "s")
        // Group 3 = effect index when explicit ID present        (e.g. "1", may be empty)
        // Group 4 = optional trailing % on explicit-ID form
        // Group 5 = conditional spell ID  ($?s12598 / $?a386763 -> "12598")
        // Group 6 = standalone variable letter on current spell  ($s1, $d, $w1 ...)
        // Group 7 = effect index for standalone var              (may be empty)
        // Group 8 = optional trailing % on standalone form
        // Group 9 = multi-character token (MWS, ctrmax, ctrmin, pl, pri, ecix, ec, proccooldown, procrppm)
        private static readonly System.Text.RegularExpressions.Regex _spellRefRegex =
            new System.Text.RegularExpressions.Regex(
                @"\$(\d{4,})([saAdwWmMeotpbhifFjJnqQruxzvV])(\d*)(%?)|\$\?[saAdwWmMeotpbhifFjJnqQruxzvV](\d{4,})|\$([saAdwWmMeotpbhifFjJnqQruxzvV])(\d*)(%?)|\$(MWS|ctrmax|ctrmin|pl|pri|ecix|ec|proccooldown|procrppm)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Matches lines from SMSG_SPELL_START / SMSG_SPELL_GO / SMSG_SPELL_PREPARE that carry a spell ID.
        // Only the authoritative "(Cast) SpellID: NNNNN" form is used; the old "Entry: NNNNN"
        // alternative was removed because it also matched creature GUID fields
        // (CasterGUID, HitTarget, etc.) causing false spell-ID lookups.
        private static readonly System.Text.RegularExpressions.Regex _castedSpellLineRegex =
            new System.Text.RegularExpressions.Regex(
                @"\(Cast\)\s+SpellID:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Matches "[N] SpellID: NNNNN" lines inside SMSG_AURA_UPDATE blocks.
        private static readonly System.Text.RegularExpressions.Regex _auraUpdateSpellLineRegex =
            new System.Text.RegularExpressions.Regex(
                @"^\s*\[\d+\]\s+SpellID:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Matches "(Cast) OriginalCastID: ... Entry: NNNN (SpellName)" to detect which class spell originated this cast.
        private static readonly System.Text.RegularExpressions.Regex _originalCastEntryRegex =
            new System.Text.RegularExpressions.Regex(
                @"\(Cast\)\s+OriginalCastID:.*\bEntry:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Matches the CasterGUID line of a cast packet and extracts the creature NPC entry ID.
        // e.g. "(Cast) CasterGUID: Full: 0x... Entry: 223453 (Arcane Phoenix) Low: ..."
        private static readonly System.Text.RegularExpressions.Regex _casterGuidEntryRegex =
            new System.Text.RegularExpressions.Regex(
                @"\(Cast\)\s+CasterGUID:.*\bEntry:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Matches the CastID GUID line to extract the spell ID from its Entry field.
        // e.g. "(Cast) CastID: Full: 0x... Cast/3 ... Entry: 450461 Low: ..."
        // The Entry of a Cast-type GUID is the spell being cast, which is the most
        // reliable source for the summoned-creature's spell ID.
        private static readonly System.Text.RegularExpressions.Regex _castIdEntryRegex =
            new System.Text.RegularExpressions.Regex(
                @"\(Cast\)\s+CastID:.*\bEntry:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string DescribeVariable(char varLetter, string indexStr, SpellInfo? refSpell, bool isPercent = false)
        {
            var pct = isPercent ? "%" : string.Empty;
            bool hasIdx = int.TryParse(indexStr, out var oneBasedIdx) && oneBasedIdx >= 1;
            int effectIndex = hasIdx ? oneBasedIdx - 1 : 0;

            SpellEffectInfo? GetEff() => hasIdx && refSpell != null
                ? refSpell.SpellEffectInfoStore.FirstOrDefault(e => e.EffectIndex == effectIndex)
                : null;

            switch (varLetter)
            {
                case 's' or 'w':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: BasePoints = {eff.EffectBasePoints:F2}{pct}";
                    return hasIdx ? $"EFFECT {effectIndex}: BasePoints (not found)" : $"s{indexStr} (base points){pct}";
                }
                case 'm':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: MinValue = {eff.EffectBasePoints:F2}{pct}";
                    return hasIdx ? $"EFFECT {effectIndex}: MinValue (not found)" : $"m{indexStr} (min value){pct}";
                }
                case 'M':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: MaxValue = {eff.EffectBasePoints:F2}{pct}";
                    return hasIdx ? $"EFFECT {effectIndex}: MaxValue (not found)" : $"M{indexStr} (max value){pct}";
                }
                case 'e':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: EffectAmplitude = {eff.EffectAmplitude:F2}{pct}";
                    return $"e{indexStr} (EffectAmplitude){pct}";
                }
                case 'o':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: TotalPeriodDamage (base={eff.EffectBasePoints:F2}, period={eff.EffectAuraPeriod} ms){pct}";
                    return $"o{indexStr} (total period damage){pct}";
                }
                case 't' or 'p':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: Period = {eff.EffectAuraPeriod} ms{pct}";
                    return $"{varLetter}{indexStr} (period){pct}";
                }
                case 'd':
                {
                    if (refSpell != null)
                    {
                        var dur = refSpell.DurationEntry?.Duration ?? 0;
                        return dur != 0 ? $"Duration: {dur} ms" : "Duration: 0";
                    }
                    return "d (duration)";
                }
                case 'a':
                {
                    var eff = GetEff();
                    if (eff != null && DBC.DBC.SpellRadius.TryGetValue((int)eff.TargetARadius, out var ra))
                        return $"EFFECT {effectIndex}: MinRadius = {ra.Radius:F2} (index {eff.TargetARadius})";
                    return $"a{indexStr} (min effect radius)";
                }
                case 'A':
                {
                    var eff = GetEff();
                    if (eff != null && DBC.DBC.SpellRadius.TryGetValue((int)eff.TargetBRadius, out var rb))
                        return $"EFFECT {effectIndex}: MaxRadius = {rb.Radius:F2} (index {eff.TargetBRadius})";
                    return $"A{indexStr} (max effect radius)";
                }
                case 'b':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: BonusFromResource (+{eff.EffectPointsPerResource:F4} per resource){pct}";
                    return $"b{indexStr} (bonus from resource){pct}";
                }
                case 'h':
                    return refSpell != null ? $"ProcChance: {refSpell.ProcChance}%" : "h (proc chance)";
                case 'i':
                {
                    if (refSpell != null && refSpell.MaxTargets != 0)
                        return $"MaxTargets: {refSpell.MaxTargets}";
                    return $"i{indexStr} (max targets)";
                }
                case 'f':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: ChainAmplitude = {eff.EffectChainAmplitude:F4} (float)";
                    return $"f{indexStr} (chain amplitude, float)";
                }
                case 'F':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: ChainAmplitude = {(int)eff.EffectChainAmplitude} (int)";
                    return $"F{indexStr} (chain amplitude, int)";
                }
                case 'j' or 'J':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: ChainTargets(jump) = {eff.EffectChainTargets}";
                    return $"{varLetter}{indexStr} (chain/jump targets)";
                }
                case 'n':
                    return refSpell != null ? $"ProcCharges: {refSpell.ProcCharges}" : "n (charges)";
                case 'q':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: MiscValueA = {eff.EffectMiscValueA}";
                    return $"q{indexStr} (miscValueA)";
                }
                case 'Q':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: MiscValueA = {eff.EffectMiscValueA} (@TODO)";
                    return $"Q{indexStr} (miscValueA, @TODO)";
                }
                case 'r':
                    return refSpell?.Range != null
                        ? $"Range: max {refSpell.Range.MaxRange[1]:F1} yd"
                        : "r (range)";
                case 'u':
                    return refSpell != null ? $"MaxStacks: {refSpell.CumulativeAura}" : "u (max stacks)";
                case 'x':
                {
                    var eff = GetEff();
                    if (eff != null) return $"EFFECT {effectIndex}: ChainTargets = {eff.EffectChainTargets}";
                    return $"x{indexStr} (chain targets)";
                }
                case 'z':
                    return "z (bind location)";
                case 'v':
                    return refSpell != null ? $"MinTargetLevel: {refSpell.SpellLevel}" : "v (min target level)";
                case 'V':
                    return refSpell != null ? $"MaxTargetLevel: {refSpell.MaxTargetLevel}" : "V (max target level)";
                default:
                    return varLetter.ToString();
            }
        }

        private static string DescribeMultiVarToken(string token, SpellInfo? spell)
        {
            return token switch
            {
                "MWS"          => "MWS (melee weapon swing time, in seconds)",
                "ctrmax"       => spell?.ContentTuningID != 0
                                  ? $"ctrmax (content tuning max level, tuning ID {spell!.ContentTuningID})"
                                  : "ctrmax (content tuning max level)",
                "ctrmin"       => spell?.ContentTuningID != 0
                                  ? $"ctrmin (content tuning min level, tuning ID {spell!.ContentTuningID})"
                                  : "ctrmin (content tuning min level)",
                "pl"           => "pl (player level)",
                "pri"          => "pri (primary stat)",
                "ecix"         => "ecix (max item level of enchantment from misc value)",
                "ec"           => "ec (calculated enchantment value from misc value)",
                "proccooldown" => spell != null
                                  ? $"proccooldown ({spell.ProcCooldown} ms)"
                                  : "proccooldown (proc cooldown)",
                "procrppm"     => spell != null
                                  ? $"procrppm ({spell.BaseProcRate:F4} per minute)"
                                  : "procrppm (procs per minute)",
                _              => token,
            };
        }

        private void WriteSpellHellParserRefs(SpellInfo spell)
        {
            _rtbSpellHellParserRefs.Clear();

            var rtb = _rtbSpellHellParserRefs;
            var boldFont = new System.Drawing.Font(rtb.Font, System.Drawing.FontStyle.Bold);
            var regularFont = new System.Drawing.Font(rtb.Font, System.Drawing.FontStyle.Regular);

            void AppendBold(string text) { rtb.SelectionFont = boldFont; rtb.AppendText(text); rtb.SelectionFont = regularFont; }
            void AppendLine(string text = "") { rtb.AppendText(text + Environment.NewLine); }

            AppendBold($"Selected spell: {spell.ID} - {spell.NameAndSubname}");
            AppendLine();

            // ---- Section 1: Casted By ----
            AppendLine();
            AppendBold("Casted by:");
            AppendLine();
            if (_spellHellParserCastedByMap.TryGetValue(spell.ID, out var castedById)
                && DBC.DBC.SpellInfoStore.TryGetValue(castedById, out var castedBySpell))
            {
                AppendBold($"  {castedById} - {castedBySpell.NameAndSubname}");
                AppendLine();
            }
            else
            {
                rtb.AppendText("  (none)" + Environment.NewLine);
            }

            // ---- Section 2: Self variables ----
            AppendLine();
            AppendBold("Spell variables (self):");
            AppendLine();

            var selfVars = new List<string>();
            foreach (var text in new[] { spell.Description, spell.Tooltip,
                spell.DescriptionVariables?.Variables ?? string.Empty })
            {
                if (string.IsNullOrEmpty(text))
                    continue;
                foreach (System.Text.RegularExpressions.Match m in _spellRefRegex.Matches(text))
                {
                    string desc;
                    if (m.Groups[9].Success)
                    {
                        desc = DescribeMultiVarToken(m.Groups[9].Value, spell);
                    }
                    else if (m.Groups[6].Success)
                    {
                        var varLetter = m.Groups[6].Value[0];
                        var indexStr = m.Groups[7].Value;
                        var isPercent = m.Groups[8].Value == "%";
                        desc = DescribeVariable(varLetter, indexStr, spell, isPercent);
                    }
                    else
                        continue;
                    if (!selfVars.Contains(desc))
                        selfVars.Add(desc);
                }
            }

            if (selfVars.Count == 0)
                rtb.AppendText("  (none)" + Environment.NewLine);
            else
                foreach (var v in selfVars)
                    rtb.AppendText($"  \u2192 {v}{Environment.NewLine}");

            // ---- Section 3: Referenced spells ----
            AppendLine();
            AppendBold("Referenced spells:");
            AppendLine();

            // Maps referenced spell ID -> deduplicated list of variable usages pulled from it
            var referencedIds = new SortedDictionary<int, List<string>>();

            void AddRef(int refId, string usage)
            {
                if (refId == spell.ID)
                    return;
                if (!referencedIds.TryGetValue(refId, out var usages))
                    referencedIds[refId] = usages = new List<string>();
                if (!usages.Contains(usage))
                    usages.Add(usage);
            }

            foreach (var text in new[] { spell.Description, spell.Tooltip,
                spell.DescriptionVariables?.Variables ?? string.Empty })
            {
                if (string.IsNullOrEmpty(text))
                    continue;

                foreach (System.Text.RegularExpressions.Match m in _spellRefRegex.Matches(text))
                {
                    if (m.Groups[1].Success)
                    {
                        // $NNNNletter[index][%] -- explicit foreign spell reference
                        if (int.TryParse(m.Groups[1].Value, out var explicitId))
                        {
                            DBC.DBC.SpellInfoStore.TryGetValue(explicitId, out var refSpellForVar);
                            var isPercent = m.Groups[4].Value == "%";
                            AddRef(explicitId, DescribeVariable(m.Groups[2].Value[0], m.Groups[3].Value, refSpellForVar, isPercent));
                        }
                    }
                    else if (m.Groups[5].Success)
                    {
                        // $?s/a NNNNN -- conditional check against another spell
                        if (int.TryParse(m.Groups[5].Value, out var condId))
                            AddRef(condId, "(conditional check)");
                    }
                }
            }

            if (referencedIds.Count == 0)
            {
                rtb.AppendText("  (none)" + Environment.NewLine);
                return;
            }

            foreach (var (refId, usages) in referencedIds)
            {
                var name = DBC.DBC.SpellInfoStore.TryGetValue(refId, out var refSpell)
                    ? refSpell.NameAndSubname
                    : "(not found)";
                AppendBold($"  {refId} - {name}");
                AppendLine();
                foreach (var usage in usages)
                    rtb.AppendText($"    \u2192 {usage}{Environment.NewLine}");
            }
        }

        private void LvSpellHellParserListRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = new ListViewItem([_spellHellParserListView[e.ItemIndex].ID.ToString(), _spellHellParserListView[e.ItemIndex].NameAndSubname]);
        }

        private void LvSpellHellParserCastedListSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvSpellHellParserCastedList.SelectedIndices.Count > 0)
            {
                var spell = _spellHellParserCastedListView[_lvSpellHellParserCastedList.SelectedIndices[0]];
                spell.Write(_rtbSpellHellParser);
                WriteSpellHellParserRefs(spell);
            }
        }

        private void LvSpellHellParserCastedListRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var spell = _spellHellParserCastedListView[e.ItemIndex];
            var fromBy = string.Empty;

            // "By:" – this spell was triggered by another class spell (OriginalCastID)
            if (_spellHellParserCastedByMap.TryGetValue(spell.ID, out var byId) &&
                DBC.DBC.SpellInfoStore.TryGetValue(byId, out var bySpell))
            {
                fromBy = $"By: {bySpell.Name}";
            }
            else
            {
                // "From:" – this spell triggered other spells (reverse lookup)
                var triggeredName = _spellHellParserCastedByMap
                    .Where(kvp => kvp.Value == spell.ID)
                    .Select(kvp => DBC.DBC.SpellInfoStore.TryGetValue(kvp.Key, out var s) ? s.Name : null)
                    .FirstOrDefault(n => n != null);
                if (triggeredName != null)
                    fromBy = $"From: {triggeredName}";
            }

            e.Item = new ListViewItem([spell.ID.ToString(), spell.NameAndSubname, fromBy]);
        }

        #endregion

        #region SPELL INFO PAGE

        private void LvSpellListSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvSpellList.SelectedIndices.Count > 0)
                _spellList[_lvSpellList.SelectedIndices[0]].Write(_rtSpellInfo);
        }

        private void TbSearchIdKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                AdvancedSearch();
        }

        private void BSearchClick(object sender, EventArgs e)
        {
            AdvancedSearch();
        }

        private void CbSpellFamilyNamesSelectedIndexChanged(object sender, EventArgs e)
        {
            if (((ComboBox)sender).SelectedIndex != 0)
                AdvancedFilter();
        }

        private void TbAdvansedFilterValKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                AdvancedFilter();
        }

        private void AdvancedSearch()
        {
            var name = _tbSearchId.Text;
            var id = name.ToUInt32();
            var ic = _tbSearchIcon.Text.ToUInt32();
            var at = _tbSearchAttributes.Text.ToUInt32();

            _spellList = (from spellInfo in DBC.DBC.SpellInfoStore.Values
                          where
                              ((id == 0 || spellInfo.ID == id) && (ic == 0 || spellInfo.SpellIconFileDataID == ic) &&
                               (at == 0 || ((uint)spellInfo.Attributes & at) != 0 || ((uint)spellInfo.AttributesEx & at) != 0 ||
                                ((uint)spellInfo.AttributesEx2 & at) != 0 || ((uint)spellInfo.AttributesEx3 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx4 & at) != 0 || ((uint)spellInfo.AttributesEx5 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx6 & at) != 0 || ((uint)spellInfo.AttributesEx7 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx8 & at) != 0 || ((uint)spellInfo.AttributesEx9 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx10 & at) != 0 || ((uint)spellInfo.AttributesEx11 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx12 & at) != 0 || ((uint)spellInfo.AttributesEx13 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx14 & at) != 0 || ((uint)spellInfo.AttributesEx15 & at) != 0 ||
                                ((uint)spellInfo.AttributesEx16 & at) != 0))
                                && ((id != 0 || ic != 0 && at != 0) || spellInfo.Name.ContainsText(name))
                          orderby spellInfo.ID
                          select spellInfo).ToList();

            _lvSpellList.VirtualListSize = _spellList.Count;
            if (_lvSpellList.SelectedIndices.Count > 0)
                _lvSpellList.Items[_lvSpellList.SelectedIndices[0]].Selected = false;

            _lvSpellList.Invalidate();
        }

        private void AdvancedFilter()
        {
            var bFamilyNames = _cbSpellFamilyName.SelectedIndex != 0;
            var fFamilyNames = _cbSpellFamilyName.SelectedValue.ToInt32();

            var bSpellAura = _cbSpellAura.SelectedIndex != 0;
            var fSpellAura = _cbSpellAura.SelectedValue.ToInt32();

            var bSpellEffect = _cbSpellEffect.SelectedIndex != 0;
            var fSpellEffect = _cbSpellEffect.SelectedValue.ToInt32();

            var bTarget1 = _cbTarget1.SelectedIndex != 0;
            var fTarget1 = _cbTarget1.SelectedValue.ToInt32();

            var bTarget2 = _cbTarget2.SelectedIndex != 0;
            var fTarget2 = _cbTarget2.SelectedValue.ToInt32();

            // additional spell effect filters
            var advEffectVal1 = _tbAdvancedEffectFilter1Val.Text;
            var advEffectVal2 = _tbAdvancedEffectFilter2Val.Text;

            var fieldEffect1 = (MemberInfo)_cbAdvancedEffectFilter1.SelectedValue;
            var fieldEffect2 = (MemberInfo)_cbAdvancedEffectFilter2.SelectedValue;

            var use1EffectVal = !string.IsNullOrEmpty(advEffectVal1);
            var use2EffectVal = !string.IsNullOrEmpty(advEffectVal2);

            var fieldEffect1Ct = (CompareType)_cbAdvancedEffectFilter1CompareType.SelectedIndex;
            var fieldEffect2Ct = (CompareType)_cbAdvancedEffectFilter2CompareType.SelectedIndex;

            var filterValEffectFn1 = FilterFactory.CreateFilterFunc<SpellEffectInfo>(fieldEffect1, advEffectVal1, fieldEffect1Ct);
            var filterValEffectFn2 = FilterFactory.CreateFilterFunc<SpellEffectInfo>(fieldEffect2, advEffectVal2, fieldEffect2Ct);

            // additional filters
            var advVal1 = _tbAdvancedFilter1Val.Text;
            var advVal2 = _tbAdvancedFilter2Val.Text;

            var field1 = (MemberInfo)_cbAdvancedFilter1.SelectedValue;
            var field2 = (MemberInfo)_cbAdvancedFilter2.SelectedValue;

            var use1Val = !string.IsNullOrEmpty(advVal1);
            var use2Val = !string.IsNullOrEmpty(advVal2);

            var field1Ct = (CompareType)_cbAdvancedFilter1CompareType.SelectedIndex;
            var field2Ct = (CompareType)_cbAdvancedFilter2CompareType.SelectedIndex;

            var filterValFn1 = FilterFactory.CreateFilterFunc<SpellInfo>(field1, advVal1, field1Ct);
            var filterValFn2 = FilterFactory.CreateFilterFunc<SpellInfo>(field2, advVal2, field2Ct);

            _spellList = DBC.DBC.SpellInfoStore.Values.Where(
                spell => (!bFamilyNames || spell.SpellFamilyName == fFamilyNames) &&
                         (!bSpellEffect || spell.HasEffect((SpellEffects)fSpellEffect)) &&
                         (!bSpellAura || spell.HasAura((AuraType)fSpellAura)) &&
                         (!bTarget1 || spell.HasTargetA((Targets)fTarget1)) &&
                         (!bTarget2 || spell.HasTargetB((Targets)fTarget2)) &&
                         (!use1Val || filterValFn1(spell)) &&
                         (!use2Val || filterValFn2(spell)) &&
                         ((!use1EffectVal && !use2EffectVal) || spell.SpellEffectInfoStore.Any(effect =>
                         (!use1EffectVal || filterValEffectFn1(effect)) &&
                         (!use2EffectVal || filterValEffectFn2(effect)))))
                .OrderBy(spell => spell.ID)
                .ToList();

            _lvSpellList.VirtualListSize = _spellList.Count;
            if (_lvSpellList.SelectedIndices.Count > 0)
                _lvSpellList.Items[_lvSpellList.SelectedIndices[0]].Selected = false;

            _lvSpellList.Invalidate();
        }

        #endregion

        #region SPELL PROC INFO PAGE

        private void NewProcSpellIdClick(object sender, EventArgs e)
        {
            var spellId = int.Parse(_tbNewProcSpellId.Text);
            var spell = DBC.DBC.SpellInfoStore[spellId];
            var proc = new SpellProcEntry()
            {
                SpellId = spellId,
                SpellName = spell.NameAndSubname,
                SpellFamilyName = (SpellFamilyNames)spell.SpellFamilyName,
                SpellFamilyMask = new uint[4],
            };
            ProcParse(proc);
        }

        private void LoadProcFromDBClick(object sender, EventArgs e)
        {
            tabControl1.SelectedIndex = 4;
            _tbLoadProcSpellId.Text = _tbNewProcSpellId.Text;
        }

        private void NewProcSpellIdTextChanged(object sender, EventArgs e)
        {
            _bNewProcSpellId.Enabled = int.TryParse(((TextBox)sender).Text, out var spellId) && DBC.DBC.SpellInfoStore.ContainsKey(spellId);
        }

        private void CbProcSpellFamilyNameSelectedIndexChanged(object sender, EventArgs e)
        {
            if (((ComboBox)sender).SelectedIndex > 0)
                ProcFilter();
        }

        private void CbProcFlagCheckedChanged(object sender, EventArgs e)
        {
            splitContainer3.SplitterDistance = ((CheckBox)sender).Checked ? 300 : 200;
        }

        private void TvFamilyTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Level > 0)
                SetProcAttribute(DBC.DBC.SpellInfoStore[e.Node.Name.ToInt32()]);
        }

        private void LvProcSpellListSelectedIndexChanged(object sender, EventArgs e)
        {
            var lv = (ListView)sender;
            if (lv.SelectedIndices.Count <= 0)
                return;
            SetProcAttribute(_spellProcList[lv.SelectedIndices[0]]);
            _lvProcAdditionalInfo.Items.Clear();
        }

        private void LvProcAdditionalInfoSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvProcAdditionalInfo.SelectedIndices.Count > 0)
                SetProcAttribute(DBC.DBC.SpellInfoStore[_lvProcAdditionalInfo.SelectedItems[0].SubItems[0].Text.ToInt32()]);
        }

        private void ClbSchoolsSelectedIndexChanged(object sender, EventArgs e)
        {
            if (ProcInfo.SpellProc == null || ProcInfo.SpellProc.ID == 0)
                return;
            _bWrite.Enabled = true;
            GetProcAttribute(ProcInfo.SpellProc);
        }

        private void TbCooldownTextChanged(object sender, EventArgs e)
        {
            if (ProcInfo.SpellProc == null || ProcInfo.SpellProc.ID == 0)
                return;
            _bWrite.Enabled = true;
            GetProcAttribute(ProcInfo.SpellProc);
        }

        private void BProcSearchClick(object sender, EventArgs e)
        {
            Search();
        }

        private void TbSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                Search();
        }

        private void TvFamilyTreeSelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedValue = ((ComboBox)sender).SelectedValue.ToInt32();
            if (selectedValue == -1)
                return;
            _tvFamilyTree.Nodes.Clear();

            // SPELLFAMILY_GENERIC proc records ignore SpellFamilyMask
            // so we don't populate TreeView for performance
            if (selectedValue == (int)SpellFamilyNames.SPELLFAMILY_GENERIC)
                return;

            ProcInfo.Fill(_tvFamilyTree, (SpellFamilyNames)selectedValue);
            PopulateProcAdditionalInfo();
        }

        private void SetProcAttribute(SpellInfo spell)
        {
            spell.Write(_rtbProcSpellInfo);
        }

        private void GetProcAttribute(SpellInfo spell)
        {
            var SpellFamilyFlags = _tvFamilyTree.GetMask();
            _lProcHeader.Text = $"Spell ({spell.ID}) {spell.NameAndSubname} ==> SpellFamily {_cbProcFitstSpellFamily.SelectedValue}, 0x{SpellFamilyFlags[0]:X8} {SpellFamilyFlags[1]:X8} {SpellFamilyFlags[2]:X8} {SpellFamilyFlags[3]:X8}";
        }

        private void Search()
        {
            var id = _tbProcSeach.Text.ToUInt32();

            _spellProcList = (from spell in DBC.DBC.SpellInfoStore.Values
                              where
                                  (id == 0 || spell.ID == id) &&
                                  (id != 0 || spell.Name.ContainsText(_tbProcSeach.Text))
                              select spell).ToList();

            _lvProcSpellList.VirtualListSize = _spellProcList.Count;
            if (_lvProcSpellList.SelectedIndices.Count > 0)
                _lvProcSpellList.Items[_lvProcSpellList.SelectedIndices[0]].Selected = false;
        }

        private void ProcFilter()
        {
            var bFamilyNames = _cbProcSpellFamilyName.SelectedIndex != 0;
            var fFamilyNames = _cbProcSpellFamilyName.SelectedValue.ToInt32();

            var bSpellAura = _cbProcSpellAura.SelectedIndex != 0;
            var fSpellAura = _cbProcSpellAura.SelectedValue.ToInt32();

            var bSpellEffect = _cbProcSpellEffect.SelectedIndex != 0;
            var fSpellEffect = _cbProcSpellEffect.SelectedValue.ToInt32();

            var bTarget1 = _cbProcTarget1.SelectedIndex != 0;
            var fTarget1 = _cbProcTarget1.SelectedValue.ToInt32();

            var bTarget2 = _cbProcTarget2.SelectedIndex != 0;
            var fTarget2 = _cbProcTarget2.SelectedValue.ToInt32();

            _spellProcList = (from spell in DBC.DBC.SpellInfoStore.Values
                              where
                                  (!bFamilyNames || spell.SpellFamilyName == fFamilyNames) &&
                                  (!bSpellEffect || spell.HasEffect((SpellEffects)fSpellEffect)) &&
                                  (!bSpellAura || spell.HasAura((AuraType)fSpellAura)) &&
                                  (!bTarget1 || spell.HasTargetA((Targets)fTarget1)) &&
                                  (!bTarget2 || spell.HasTargetB((Targets)fTarget2))
                              orderby spell.ID
                              select spell).ToList();

            _lvProcSpellList.VirtualListSize = _spellProcList.Count();
            if (_lvProcSpellList.SelectedIndices.Count > 0)
                _lvProcSpellList.Items[_lvProcSpellList.SelectedIndices[0]].Selected = false;
        }

        private void FamilyTreeAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (ProcInfo.SpellProc == null || !ProcInfo.Update)
                return;

            _bWrite.Enabled = true;

            PopulateProcAdditionalInfo();

            GetProcAttribute(ProcInfo.SpellProc);
        }

        private void PopulateProcAdditionalInfo()
        {
            _lvProcAdditionalInfo.Items.Clear();
            _lvProcAdditionalInfo.Items.AddRange(
                _tvFamilyTree.Nodes.Cast<TreeNode>()
                    .Where(familyBitNode => familyBitNode.Checked)
                    .SelectMany(familyBitNode => familyBitNode.Nodes.Cast<TreeNode>())
                    .Distinct()
                    .Select(familySpellNode =>
                    {
                        var spell = DBC.DBC.SpellInfoStore[familySpellNode.Name.ToInt32()];

                        return new ListViewItem(new[] { familySpellNode.Name, spell.NameAndSubname, spell.Description })
                        {
                            ImageKey = familySpellNode.ImageKey
                        };
                    })
                    .OrderBy(listViewItem => listViewItem.SubItems[0].Text.ToInt32())
                    .ToArray());
        }

        #endregion

        #region COMPARE PAGE

        private void CompareFilterSpellTextChanged(object sender, EventArgs e)
        {
            var spell1 = _tbCompareFilterSpell1.Text.ToInt32();
            var spell2 = _tbCompareFilterSpell2.Text.ToInt32();

            if (DBC.DBC.SpellInfoStore.ContainsKey(spell1) && DBC.DBC.SpellInfoStore.ContainsKey(spell2))
                SpellCompare.Compare(_rtbCompareSpell1, _rtbCompareSpell2, DBC.DBC.SpellInfoStore[spell1], DBC.DBC.SpellInfoStore[spell2]);
        }

        private void CompareSearch1Click(object sender, EventArgs e)
        {
            var form = new FormSearch();
            form.ShowDialog(this);
            if (form.DialogResult == DialogResult.OK)
                _tbCompareFilterSpell1.Text = form.Spell.ID.ToString();
            form.Dispose();
        }

        private void CompareSearch2Click(object sender, EventArgs e)
        {
            var form = new FormSearch();
            form.ShowDialog(this);
            if (form.DialogResult == DialogResult.OK)
                _tbCompareFilterSpell2.Text = form.Spell.ID.ToString();
            form.Dispose();
        }

        #endregion

        #region SQL PAGE

        private void SqlDataListMouseDoubleClick(object sender, MouseEventArgs e)
        {
            ProcParse(sender);
        }

        private void SqlDataListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                ProcParse(sender);
        }

        private void SqlToBaseClick(object sender, EventArgs e)
        {
            if (MySqlConnection.Connected)
                MySqlConnection.Insert(_rtbSqlLog.Text);
            else
                MessageBox.Show(@"Can't connect to database!", @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SqlSaveClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_rtbSqlLog.Text))
                return;

            var sd = new SaveFileDialog { Filter = @"SQL files|*.sql" };
            if (sd.ShowDialog() == DialogResult.OK)
                using (var sw = new StreamWriter(sd.FileName, false, Encoding.UTF8))
                    sw.Write(_rtbSqlLog.Text);
        }

        private void CalcProcFlagsClick(object sender, EventArgs e)
        {
            switch (((Button)sender).Name)
            {
                case "_bSqlSchool":
                {
                    var val = _tbSqlSchool.Text.ToUInt32();
                    var form = new FormCalculateFlags(typeof(SpellSchools), val, string.Empty);
                    form.ShowDialog(this);
                    if (form.DialogResult == DialogResult.OK)
                        _tbSqlSchool.Text = form.Flags.ToString();
                    break;
                }
                case "_bSqlProc":
                {
                    var val = _tbSqlProc.Text.ToUInt32();
                    var form = new FormCalculateFlags(typeof(ProcFlags), val, "PROC_FLAG_");
                    form.ShowDialog(this);
                    if (form.DialogResult == DialogResult.OK)
                        _tbSqlProc.Text = form.Flags.ToString();
                    break;
                }
                case "_bSqlProcFlagsHit":
                {
                    var val = _tbSqlProcFlagsHit.Text.ToUInt32();
                    var form = new FormCalculateFlags(typeof(ProcFlagsHit), val, "PROC_HIT_");
                    form.ShowDialog(this);
                    if (form.DialogResult == DialogResult.OK)
                        _tbSqlProcFlagsHit.Text = form.Flags.ToString();
                    break;
                }
            }
        }

        private void SelectClick(object sender, EventArgs e)
        {
            if (!MySqlConnection.Connected)
            {
                MessageBox.Show(@"Can't connect to database!", @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var compare = _cbBinaryCompare.Checked ? "&" : "=";

            var conditions = new List<string>();
            if (DBC.DBC.SpellInfoStore.ContainsKey(_tbLoadProcSpellId.Text.ToInt32()))
                conditions.Add($"SpellId = {_tbLoadProcSpellId.Text.ToInt32()}");

            if (_cbSqlSpellFamily.SelectedValue.ToInt32() != -1)
                conditions.Add($"SpellFamilyName = {_cbSqlSpellFamily.SelectedValue.ToInt32()}");

            if (_tbSqlSchool.Text.ToUInt32() != 0)
                conditions.Add($"SchoolMask {compare} {_tbSqlSchool.Text.ToUInt32()}");

            if (_tbSqlProc.Text.ToUInt32() != 0)
                conditions.Add($"ProcFlags {compare} {_tbSqlProc.Text.ToUInt32()}");

            if (_tbSqlProcFlagsHit.Text.ToUInt32() != 0)
                conditions.Add($"HitMask {compare} {_tbSqlProcFlagsHit.Text.ToUInt32()}");

            var subquery = "WHERE 1=1";
            if (conditions.Count > 0)
                subquery += conditions.Aggregate("", (sql, condition) => sql + " && " + condition);
            else if (!string.IsNullOrEmpty(_tbSqlManual.Text))
                subquery += " && " + _tbSqlManual.Text;

            var query = "SELECT SpellId, SchoolMask, SpellFamilyName, SpellFamilyMask0, SpellFamilyMask1, SpellFamilyMask2, SpellFamilyMask3, "
                        + $"ProcFlags, SpellTypeMask, SpellPhaseMask, HitMask, AttributesMask, DisableEffectsMask, ProcsPerMinute, Chance, Cooldown, Charges FROM `spell_proc` {subquery} ORDER BY SpellId";
            try
            {
                MySqlConnection.SelectProc(query);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }

            _lvDataList.VirtualListSize = MySqlConnection.SpellProcEvent.Count;
            if (_lvDataList.SelectedIndices.Count > 0)
                _lvDataList.Items[_lvDataList.SelectedIndices[0]].Selected = false;

            // check bad spell and drop
            foreach (var str in MySqlConnection.Dropped)
                _rtbSqlLog.AppendText(str);

            if (MySqlConnection.Dropped.Count != 0)
                _rtbSqlLog.AppendLine();

            _rtbSqlLog.ColorizeCode();
        }

        private void WriteClick(object sender, EventArgs e)
        {
            if (ProcInfo.SpellProc == null)
                return;

            var spellFamilyFlags = _tvFamilyTree.GetMask();

            // spell comment
            var comment = $" -- {ProcInfo.SpellProc.NameAndSubname}";

            // drop query
            var drop = $"DELETE FROM `spell_proc` WHERE `SpellId` IN ({ProcInfo.SpellProc.ID});";

            // insert query
            var procFlags = _clbProcFlags.GetFlagsValue(0) != ProcInfo.SpellProc.ProcFlags ? _clbProcFlags.GetFlagsValue(0) : 0;
            var procFlags2 = _clbProcFlags.GetFlagsValue(1) != ProcInfo.SpellProc.ProcFlagsEx ? _clbProcFlags.GetFlagsValue(1) : 0;
            var procPPM = _tbPPM.Text.Replace(',', '.') != ProcInfo.SpellProc.BaseProcRate.ToString(CultureInfo.InvariantCulture) ? _tbPPM.Text.Replace(',', '.') : "0";
            var procChance = _tbChance.Text.Replace(',', '.') != ProcInfo.SpellProc.ProcChance.ToString() ? _tbChance.Text.Replace(',', '.') : "0";
            var procCooldown = _tbCooldown.Text.ToUInt32() != ProcInfo.SpellProc.ProcCooldown ? _tbCooldown.Text.ToUInt32() : 0;
            var procCharges = _tbProcCharges.Text.ToInt32() != ProcInfo.SpellProc.ProcCharges ? _tbProcCharges.Text.ToInt32() : 0;

            var insert = "INSERT INTO `spell_proc` (`SpellId`,`SchoolMask`,`SpellFamilyName`,`SpellFamilyMask0`,`SpellFamilyMask1`,`SpellFamilyMask2`,`SpellFamilyMask3`,`ProcFlags`,`ProcFlags2`,`SpellTypeMask`,`SpellPhaseMask`,`HitMask`,`AttributesMask`,`DisableEffectsMask`,`ProcsPerMinute`,`Chance`,`Cooldown`,`Charges`) VALUES\r\n"
                + $"({ProcInfo.SpellProc.ID},0x{_clbSchools.GetFlagsValue():X2},"
                + $"{_cbProcFitstSpellFamily.SelectedValue.ToUInt32()},0x{spellFamilyFlags[0]:X8},0x{spellFamilyFlags[1]:X8},0x{spellFamilyFlags[2]:X8},0x{spellFamilyFlags[3]:X8},"
                + $"0x{procFlags:X},0x{procFlags2:X},0x{_clbSpellTypeMask.GetFlagsValue():X},0x{_clbSpellPhaseMask.GetFlagsValue():X},0x{_clbProcFlagHit.GetFlagsValue():X},0x{_clbProcAttributes.GetFlagsValue():X},0x0,{procPPM},{procChance},{procCooldown},{procCharges});";

            _rtbSqlLog.AppendText(drop + "\r\n" + insert + comment + "\r\n\r\n");
            _rtbSqlLog.ColorizeCode();
            if (MySqlConnection.Connected)
                MySqlConnection.Insert(drop + insert);

            ((Button)sender).Enabled = false;
        }

        private void ProcParse(object sender)
        {
            ProcParse(MySqlConnection.SpellProcEvent[((ListView)sender).SelectedIndices[0]]);
        }

        private void ProcParse(SpellProcEntry proc)
        {
            var spell = DBC.DBC.SpellInfoStore[Math.Abs(proc.SpellId)];
            ProcInfo.SpellProc = spell;

            spell.Write(_rtbProcSpellInfo);

            _tbNewProcSpellId.Text = proc.SpellId.ToString();

            _clbSchools.SetCheckedItemFromFlag((uint)proc.SchoolMask);
            _clbProcFlags.SetCheckedItemFromFlag((uint)proc.ProcFlags, 0);
            _clbProcFlags.SetCheckedItemFromFlag((uint)proc.ProcFlags2, 1);
            _clbSpellTypeMask.SetCheckedItemFromFlag((uint)proc.SpellTypeMask);
            _clbSpellPhaseMask.SetCheckedItemFromFlag((uint)proc.SpellPhaseMask);
            _clbProcFlagHit.SetCheckedItemFromFlag((uint)proc.HitMask);
            _clbProcAttributes.SetCheckedItemFromFlag((uint)proc.AttributesMask);

            _cbProcSpellFamilyTree.SelectedValue = (uint)proc.SpellFamilyName;
            _cbProcFitstSpellFamily.SelectedValue = (uint)proc.SpellFamilyName;

            _tbPPM.Text = proc.ProcsPerMinute.ToString(CultureInfo.InvariantCulture);
            _tbChance.Text = proc.Chance.ToString(CultureInfo.InvariantCulture);
            _tbCooldown.Text = proc.Cooldown.ToString();
            _tbProcCharges.Text = proc.Charges.ToString();

            _tvFamilyTree.SetMask(proc.SpellFamilyMask);
            PopulateProcAdditionalInfo();

            tabControl1.SelectedIndex = 3;
        }

        #endregion

        #region VIRTUAL MODE

        private List<SpellInfo> _spellList = new List<SpellInfo>();

        private List<SpellInfo> _spellProcList = new List<SpellInfo>();

        private void LvSpellListRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = new ListViewItem([string.Empty, _spellList[e.ItemIndex].ID.ToString(), _spellList[e.ItemIndex].NameAndSubname]);
        }

        private void LvProcSpellListRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = new ListViewItem([_spellProcList[e.ItemIndex].ID.ToString(), _spellProcList[e.ItemIndex].NameAndSubname]);
        }

        private void LvSqlDataRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = new ListViewItem(MySqlConnection.SpellProcEvent[e.ItemIndex].ToArray());
        }

        #endregion

        public void Unblock()
        {
            tabControl1.Enabled = true;
            _bLevelScaling.Enabled = true;
        }

        public void SetLoadingProgress(int progress)
        {
            loadingProgressBar1.Value = progress;
            if (progress == 100)
            {
                loadingProgressBar1.Enabled = false;
                loadingProgressBar1.Visible = false;
                loadingProgressLabel1.Visible = false;
            }
        }
    }
}
