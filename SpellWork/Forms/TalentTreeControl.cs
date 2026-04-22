using SpellWork.DBC;
using SpellWork.Spell;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SpellWork.Forms
{
    public sealed class TalentNode
    {
        public int   Id  { get; init; }
        public float Col { get; init; }
        public int   Row { get; init; }
        public int CurrentRank { get; set; }
        public int MaxRank { get; init; } = 1;
        public int SpellId { get; init; }
        public Image? Icon { get; set; }
        public string Name { get; init; } = string.Empty;
        public bool IsGate { get; init; }
        public List<int> ChildIds { get; init; } = new();

        // Choice-node support -----------------------------------------------
        public int    AltSpellId { get; init; }
        public string AltName    { get; init; } = string.Empty;
        public Image? AltIcon    { get; set; }
        public int    SelectedChoiceIndex { get; set; }

        public bool   IsChoiceNode  => AltSpellId > 0;
        public int    ActiveSpellId => SelectedChoiceIndex == 1 && AltSpellId > 0 ? AltSpellId : SpellId;
        public string ActiveName    => SelectedChoiceIndex == 1 && !string.IsNullOrEmpty(AltName) ? AltName : Name;
        public Image? ActiveIcon    => SelectedChoiceIndex == 1 && AltIcon != null ? AltIcon : Icon;
    }

    public sealed class TalentTree
    {
        public string Name { get; init; } = string.Empty;
        public List<TalentNode> Nodes { get; init; } = new();
    }

    // ---------------------------------------------------------------------------
    // Hero-talent selection data
    // ---------------------------------------------------------------------------
    internal sealed record SpecEntry(string Name, string[] HeroTalents);
    internal sealed record ClassEntry(string Name, SpecEntry[] Specs);

    public sealed class TalentTreeControl : Panel
    {
        private int NodeDiameter = 30;
        private int GateDiameter  = 39;
        private int CellW         = 54;
        private int CellH         = 54;
        private int _treeGap      = 28;  // computed dynamically to fill available width
        private const int HeaderH = 32;
        private const int TopPad = 6;
        private const int TreeGap = 28;

        // ?? Class icon bar ????????????????????????????????????????????????
        private const int IconBarHeight  = 52;   // total bar height in pixels
        private const int IconSize       = 38;   // icon tile size
        private const int IconPad        = 14;   // gap between tiles
        private const int IconCornerR    = 4;    // rounded-rect corner radius

        // Loaded once; sorted alphabetically by file name (matches image order)
        private Image[]? _classIcons;
        private string[]? _classIconNames;   // tooltip names derived from file stem

        private int _iconBarHoverIndex = -1;  // index of hovered icon (-1 = none)
        private int _selectedIconIndex  = -1;  // index of selected (clicked) icon

        // Raised when the user picks class → spec → hero talent from the dropdown
        public event Action<string, string, string>? ClassSpecHeroSelected;

        // Raised when the user right-clicks a node and chooses "Open in Spell Info"
        public event Action<TalentNode>? NodeOpenInSpellInfo;

        // Raised when the user changes status or notes on a node
        public event Action<TalentNode, string, string>? NodeStatusNotesUpdated;

        // Delegate to retrieve current status/notes for a spell ID from the host
        public Func<int, (string status, string notes)>? QuerySpellStatusNotes;

        // Delegate: given a talent spell ID, returns sniff-matched spells related to it
        // (same name buffs, triggered spells observed in the capture, etc.)
        public Func<int, IReadOnlyList<Spell.SpellInfo>>? QueryImplementationSpells { get; set; }

        // Delegate: given a creature NPC entry ID, returns the raw set of spell IDs that
        // creature was observed casting in the sniff (includes IDs not present in DBC).
        public Func<int, IReadOnlyCollection<int>>? QuerySummonedCreatureCasts { get; set; }

        // Current selection
        private string _selectedSpec = string.Empty;
        private string _selectedHero = string.Empty;

        // ---------------------------------------------------------------------------
        // Static class / spec / hero-talent catalogue
        // ---------------------------------------------------------------------------
        internal static readonly ClassEntry[] AllClasses =
        [
            new("Death Knight", [
                new("Blood",  ["Deathbringer", "San'layn"]),
                new("Frost",  ["Deathbringer", "Rider of the Apocalypse"]),
                new("Unholy", ["Rider of the Apocalypse", "San'layn"]),
            ]),
            new("Demon Hunter", [
                new("Havoc",      ["Aldrachi Reaver", "Fel-Scarred"]),
                new("Vengeance",  ["Aldrachi Reaver", "Fel-Scarred"]),
            ]),
            new("Druid", [
                new("Balance",     ["Elune's Chosen",    "Keeper of the Grove"]),
                new("Feral",       ["Druid of the Claw", "Wildstalker"]),
                new("Guardian",    ["Druid of the Claw", "Elune's Chosen"]),
                new("Restoration", ["Keeper of the Grove", "Wildstalker"]),
            ]),
            new("Evoker", [
                new("Devastation",  ["Flameshaper",    "Scalecommander"]),
                new("Preservation", ["Chronowarden",   "Flameshaper"]),
                new("Augmentation", ["Chronowarden",   "Scalecommander"]),
            ]),
            new("Hunter", [
                new("Beast Mastery",  ["Dark Ranger", "Pack Leader"]),
                new("Marksmanship",   ["Dark Ranger", "Sentinel"]),
                new("Survival",       ["Pack Leader",  "Sentinel"]),
            ]),
            new("Mage", [
                new("Arcane", ["Spellslinger", "Sunfury"]),
                new("Fire",   ["Frostfire",    "Sunfury"]),
                new("Frost",  ["Frostfire",    "Spellslinger"]),
            ]),
            new("Monk", [
                new("Brewmaster",  ["Master of Harmony",          "Shado-Pan"]),
                new("Mistweaver",  ["Conduit of the Celestials",  "Master of Harmony"]),
                new("Windwalker",  ["Conduit of the Celestials",  "Shado-Pan"]),
            ]),
            new("Paladin", [
                new("Holy",       ["Herald of the Sun", "Lightsmith"]),
                new("Protection", ["Lightsmith",        "Templar"]),
                new("Retribution",["Herald of the Sun", "Templar"]),
            ]),
            new("Priest", [
                new("Discipline", ["Oracle",  "Voidweaver"]),
                new("Holy",       ["Archon",  "Oracle"]),
                new("Shadow",     ["Archon",  "Voidweaver"]),
            ]),
            new("Rogue", [
                new("Assassination", ["Deathstalker", "Fatebound"]),
                new("Outlaw",        ["Fatebound",    "Trickster"]),
                new("Subtlety",      ["Deathstalker", "Trickster"]),
            ]),
            new("Shaman", [
                new("Elemental",   ["Farseer",      "Stormbringer"]),
                new("Enhancement", ["Stormbringer",  "Totemic"]),
                new("Restoration", ["Farseer",       "Totemic"]),
            ]),
            new("Warlock", [
                new("Affliction",  ["Hellcaller",    "Soul Harvester"]),
                new("Demonology",  ["Diabolist",     "Soul Harvester"]),
                new("Destruction", ["Diabolist",     "Hellcaller"]),
            ]),
            new("Warrior", [
                new("Arms",       ["Colossus",      "Slayer"]),
                new("Fury",       ["Mountain Thane", "Slayer"]),
                new("Protection", ["Colossus",      "Mountain Thane"]),
            ]),
        ];

        // Normalized-name (lowercase, no spaces) → ClassEntry lookup built lazily
        private Dictionary<string, ClassEntry>? _classEntryMap;

        private Dictionary<string, ClassEntry> ClassEntryMap =>
            _classEntryMap ??= AllClasses.ToDictionary(
                c => c.Name.ToLowerInvariant().Replace(" ", ""),
                c => c);

        /// Returns the ClassEntry that matches icon at <paramref name="iconIndex"/>, or null.
        private ClassEntry? GetClassEntry(int iconIndex)
        {
            if (_classIconNames == null || iconIndex < 0 || iconIndex >= _classIconNames.Length)
                return null;
            var key = _classIconNames[iconIndex].ToLowerInvariant().Replace(" ", "");
            return ClassEntryMap.TryGetValue(key, out var entry) ? entry : null;
        }

        // Total vertical offset that trees are pushed down by
        private int TreeOffsetY => IconBarHeight;

        private IReadOnlyList<TalentTree> _trees = Array.Empty<TalentTree>();
        private TalentNode? _hovered;
        private Point       _mousePos;
        private readonly ToolTip _tip = new();

        // Spell IDs seen in the most-recently parsed sniff capture
        private HashSet<int> _sniffMatchedSpellIds = new();

        /// <summary>
        /// When false the blue sniff-match rings are hidden even if spell IDs are loaded.
        /// </summary>
        public bool ShowSniffRings { get; set; } = true;

        public TalentTreeControl()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(12, 12, 18);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            LoadClassIcons();

            _tip.AutoPopDelay = 12000;
            _tip.InitialDelay = 350;
            _tip.ReshowDelay  = 100;
            _tip.ShowAlways   = true;
        }

        /// <summary>
        /// Walks up the directory tree from <see cref="AppContext.BaseDirectory"/> until it
        /// finds a <c>SQL\<paramref name="relativePath"/></c> sub-directory, returning
        /// <c>null</c> when nothing is found.
        /// </summary>
        private static string? FindSqlSubDir(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "SQL", relativePath);
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private void LoadClassIcons()
        {
            // Icons live in SQL/classIcons/ relative to wherever the SQL folder is.
            // FindSqlSubDir walks up from AppContext.BaseDirectory so this works both
            // in development (bin\Debug\...) and in production (exe next to SQL\).
            var dir = FindSqlSubDir("classIcons")
                   ?? Path.Combine(AppContext.BaseDirectory, "talentTracker", "classIcons");

            if (!Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir, "*.jpg")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _classIcons     = new Image[files.Length];
            _classIconNames = new string[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                // Keep a copy so the file handle is freed immediately
                using var tmp = Image.FromFile(files[i]);
                var bmp = new Bitmap(tmp);
                _classIcons[i]     = bmp;
                // "class_death_knight.jpg" → "Death Knight"
                _classIconNames[i] = System.Globalization.CultureInfo.CurrentCulture
                    .TextInfo.ToTitleCase(
                        Path.GetFileNameWithoutExtension(files[i])
                            .Replace("class_", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("_", " "));
            }
        }

        public void SetTrees(IReadOnlyList<TalentTree> trees)
        {
            _trees = trees;
            ComputeLayout();
            AutoScrollMinSize = ComputeCanvasSize();
            Invalidate();
        }

        /// <summary>
        /// Highlights every talent node whose SpellId is contained in
        /// <paramref name="ids"/> with a bright-blue ring.
        /// </summary>
        public void SetSniffMatchedSpellIds(IEnumerable<int> ids)
        {
            _sniffMatchedSpellIds = new HashSet<int>(ids);
            Invalidate();
        }

        // ── Auto-sizing ────────────────────────────────────────────────────

        /// <summary>
        /// Recomputes CellW/CellH (and derived icon sizes) so the three trees
        /// fill the available control area without requiring scrolling.
        /// </summary>
        private void ComputeLayout()
        {
            if (_trees.Count == 0 || ClientSize.Width < 60 || ClientSize.Height < 60)
                return;

            // Total normalised columns across all trees
            int totalCols = 0;
            int maxRows   = 0;
            foreach (var t in _trees)
            {
                if (t.Nodes.Count == 0) { totalCols += 1; continue; }
                totalCols += (int)(t.Nodes.Max(n => n.Col) + 1);
                maxRows    = Math.Max(maxRows, t.Nodes.Max(n => n.Row) + 1);
            }
            if (totalCols == 0 || maxRows == 0) return;

            // Step 1 – cell size: constrained by both axes (keep square).
            // Reserve a small minimum gap so trees never touch edges.
            const int minGap = 8;
            int availH     = ClientSize.Height - TreeOffsetY - HeaderH - TopPad - 32;
            int availW     = ClientSize.Width  - (_trees.Count + 1) * minGap;
            int cellFromH  = availH / maxRows;
            int cellFromW  = totalCols > 0 ? availW / totalCols : cellFromH;
            int cell       = Math.Max(20, Math.Min(cellFromW, cellFromH));

            CellW        = cell;
            CellH        = cell;
            NodeDiameter = Math.Max(12, (int)(cell * 0.56f));
            GateDiameter = Math.Max(16, (int)(cell * 0.72f));

            // Step 2 – gap: distribute all remaining horizontal space evenly
            // so the left tree starts at the left edge and the right tree ends
            // at the right edge.
            int totalTreeW = _trees.Sum(t =>
                t.Nodes.Count == 0 ? CellW : (int)((t.Nodes.Max(n => n.Col) + 1) * CellW));
            int remaining = ClientSize.Width - totalTreeW;
            _treeGap = Math.Max(minGap, remaining / (_trees.Count + 1));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_trees.Count > 0)
            {
                ComputeLayout();
                AutoScrollMinSize = ComputeCanvasSize();
                Invalidate();
            }
        }

        // ?? Layout helpers ????????????????????????????????????????????????

        private int TreeWidth(TalentTree t) =>
            t.Nodes.Count == 0 ? CellW : (int)((t.Nodes.Max(n => n.Col) + 1) * CellW);

        private Size ComputeCanvasSize()
        {
            if (_trees.Count == 0) return Size.Empty;
            int totalW = _treeGap;
            int maxH = 0;
            foreach (var t in _trees)
            {
                totalW += TreeWidth(t) + _treeGap;
                if (t.Nodes.Count == 0) continue;
                int h = TreeOffsetY + HeaderH + TopPad + (t.Nodes.Max(n => n.Row) + 1) * CellH + 24;
                if (h > maxH) maxH = h;
            }
            return new Size(totalW, maxH);
        }

        private (int x, int y) NodeCenter(TalentNode n, int ox) =>
            ((int)(ox + n.Col * CellW + CellW / 2f),
             TreeOffsetY + HeaderH + TopPad + n.Row * CellH + CellH / 2);

        // ?? Paint ?????????????????????????????????????????????????????????

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Icon bar is fixed – paint before the scroll transform.
            PaintIconBar(g);

            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            int ox = _treeGap;
            foreach (var tree in _trees)
            {
                PaintTree(g, tree, ox);
                ox += TreeWidth(tree) + _treeGap;
            }

            g.ResetTransform();
            PaintNodeTooltip(g);
        }

        private void PaintIconBar(Graphics g)
        {
            if (_classIcons == null || _classIcons.Length == 0)
                return;

            int n        = _classIcons.Length;
            int barW     = ClientSize.Width;   // bar spans visible width
            int totalIconW = n * IconSize + (n - 1) * IconPad;
            int startX   = Math.Max(TreeGap, barW / 2 - totalIconW / 2);
            int iconY    = (IconBarHeight - IconSize) / 2;   // vertically centred in bar

            // Dark bar background
            using (var barBg = new SolidBrush(Color.FromArgb(200, 8, 8, 14)))
                g.FillRectangle(barBg, 0, 0, barW, IconBarHeight);

            // Bottom separator line
            using (var sep = new Pen(Color.FromArgb(80, 100, 80, 30), 1f))
                g.DrawLine(sep, 0, IconBarHeight - 1, barW, IconBarHeight - 1);

            for (int i = 0; i < n; i++)
            {
                int ix   = startX + i * (IconSize + IconPad);
                var tile = new Rectangle(ix, iconY, IconSize, IconSize);
                bool hov = (i == _iconBarHoverIndex);
                bool sel = (i == _selectedIconIndex);

                // Tile background
                using (var tileBg = new SolidBrush(
                    sel ? Color.FromArgb(200, 80, 65, 10)
                    : hov ? Color.FromArgb(180, 60, 50, 20)
                        : Color.FromArgb(160, 22, 22, 30)))
                using (var tilePath = RoundedRect(tile, IconCornerR))
                    g.FillPath(tileBg, tilePath);

                // Icon image clipped to rounded rect
                var saved = g.Save();
                using (var clip = RoundedRect(tile, IconCornerR))
                    g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(_classIcons[i], tile);
                g.Restore(saved);

                // Border
                var borderCol = sel
                    ? Color.FromArgb(255, 230, 190, 60)
                    : hov
                    ? Color.FromArgb(220, 200, 160, 40)
                    : Color.FromArgb(130, 70, 70, 80);
                using (var bp  = new Pen(borderCol, (hov || sel) ? 1.5f : 1f))
                using (var bPath = RoundedRect(tile, IconCornerR))
                    g.DrawPath(bp, bPath);
            }
        }

        // Builds a GraphicsPath for a rectangle with uniform rounded corners.
        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X,             r.Y,              d, d, 180, 90);
            p.AddArc(r.Right - d,     r.Y,              d, d, 270, 90);
            p.AddArc(r.Right - d,     r.Bottom - d,     d, d,   0, 90);
            p.AddArc(r.X,             r.Bottom - d,     d, d,  90, 90);
            p.CloseFigure();
            return p;
        }

        private void PaintTree(Graphics g, TalentTree tree, int ox)
        {
            int tw = TreeWidth(tree);

            // Centred header – positioned below the fixed icon bar
            using var hf = new Font("Segoe UI", 11f, FontStyle.Bold);
            var hs = g.MeasureString(tree.Name, hf);
            g.DrawString(tree.Name, hf, Brushes.WhiteSmoke,
                ox + tw / 2f - hs.Width / 2f, TreeOffsetY + TopPad);

            var lookup = tree.Nodes.ToDictionary(n => n.Id);

            // Edges drawn first (behind nodes)
            foreach (var node in tree.Nodes)
            {
                var (px, py) = NodeCenter(node, ox);
                foreach (var cid in node.ChildIds)
                {
                    if (!lookup.TryGetValue(cid, out var child)) continue;
                    var (cx, cy) = NodeCenter(child, ox);
                    PaintEdge(g, px, py, cx, cy, node.CurrentRank > 0);
                }
            }

            // Nodes on top
            foreach (var node in tree.Nodes)
            {
                var (cx, cy) = NodeCenter(node, ox);
                PaintNode(g, node, cx, cy, node == _hovered);
            }
        }

        private void PaintEdge(Graphics g, int x1, int y1, int x2, int y2, bool active)
        {
            var col = Color.FromArgb(220, 195, 155, 42);

            using var pen = new Pen(col, 2f);
            g.DrawLine(pen, x1, y1, x2, y2);

            // Arrowhead pointing toward child node
            float dx = x2 - x1, dy = y2 - y1;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;
            float ux = dx / len, uy = dy / len;
            float tipOffset = NodeDiameter / 2f + 5f;
            float ax = x2 - ux * tipOffset, ay = y2 - uy * tipOffset;
            float px = -uy * 5f, py2 = ux * 5f;
            using var ab = new SolidBrush(col);
            g.FillPolygon(ab, new PointF[]
            {
                new(ax, ay),
                new(ax - ux * 9f + px,  ay - uy * 9f + py2),
                new(ax - ux * 9f - px,  ay - uy * 9f - py2),
            });
        }

        private void PaintNode(Graphics g, TalentNode node, int cx, int cy, bool hovered)
        {
            int sz = node.IsGate ? GateDiameter : NodeDiameter;
            int half = sz / 2;
            var r = new Rectangle(cx - half, cy - half, sz, sz);

            bool active = node.CurrentRank > 0;
            bool maxed = node.CurrentRank >= node.MaxRank;

            // Drop shadow
            using (var sh = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                g.FillEllipse(sh, r.X + 2, r.Y + 2, r.Width, r.Height);

            // Background
            using (var bg = new SolidBrush(active
                    ? Color.FromArgb(210, 55, 46, 14)
                    : Color.FromArgb(200, 18, 18, 26)))
                g.FillEllipse(bg, r);

            // Icon (clipped to circle shape)
            var displayIcon = node.ActiveIcon;
            if (displayIcon != null)
            {
                var ir = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
                var saved = g.Save();
                using var clipPath = new GraphicsPath();
                clipPath.AddEllipse(ir);
                g.SetClip(clipPath, CombineMode.Intersect);
                g.DrawImage(displayIcon, ir);
                g.Restore(saved);
            }
            else
            {
                // Placeholder gradient when no icon
                var inner = new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);
                using var lgb = new LinearGradientBrush(inner,
                    active ? Color.FromArgb(120, 180, 140, 40) : Color.FromArgb(80, 60, 60, 70),
                    active ? Color.FromArgb(60, 100, 80, 20) : Color.FromArgb(40, 30, 30, 40),
                    LinearGradientMode.Vertical);
                using var gradPath = new GraphicsPath();
                gradPath.AddEllipse(inner);
                g.FillPath(lgb, gradPath);
            }

            // Border ring – colour driven by implementation status
            Color borderCol;
            bool isStatusColored = false;
            if (hovered)
            {
                borderCol = Color.White;
            }
            else
            {
                var (nodeStatus, _) = QuerySpellStatusNotes?.Invoke(node.ActiveSpellId) ?? ("nyi", string.Empty);
                (borderCol, isStatusColored) = nodeStatus switch
                {
                    "done" => (Color.FromArgb(255,  80, 220,  80), true),
                    "wip"  => (Color.FromArgb(255, 235, 200,  45), true),
                    "nyi"  => (Color.FromArgb(255, 220,  60,  60), true),
                    _      => (Color.FromArgb(220, 195, 155,  42), false),
                };
            }

            // Thick primary ring
            float ringW = node.IsGate ? 4f : 3f;
            using (var bp = new Pen(borderCol, ringW))
                g.DrawEllipse(bp, r);

            // For status-coloured nodes: a softer outer glow ring to make it pop
            if (isStatusColored || hovered)
            {
                int pad = node.IsGate ? 4 : 3;
                var outerR = new Rectangle(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2);
                var glowColor = hovered
                    ? Color.FromArgb(90, 255, 255, 255)
                    : Color.FromArgb(80, borderCol.R, borderCol.G, borderCol.B);
                using var glowPen = new Pen(glowColor, node.IsGate ? 3f : 2.5f);
                g.DrawEllipse(glowPen, outerR);
            }

            // Sniff-match indicator: bright electric-blue outer ring
            if (ShowSniffRings && node.ActiveSpellId > 0 && _sniffMatchedSpellIds.Contains(node.ActiveSpellId))
            {
                int bp = node.IsGate ? 8 : 7;
                var blueR = new Rectangle(r.X - bp, r.Y - bp, r.Width + bp * 2, r.Height + bp * 2);
                // Soft halo
                using var blueHalo = new Pen(Color.FromArgb(80, 40, 180, 255), node.IsGate ? 6f : 5f);
                g.DrawEllipse(blueHalo, blueR);
                // Bright ring
                using var blueRing = new Pen(Color.FromArgb(255, 40, 200, 255), 2.2f);
                g.DrawEllipse(blueRing, blueR);
            }

            // Choice-node badge: two small overlapping circles at bottom-right
            if (node.IsChoiceNode)
            {
                int bsz = Math.Max(8, sz / 5);
                int bx  = r.Right  - bsz / 2 - 1;
                int by  = r.Bottom - bsz / 2 - 1;
                var b1  = new Rectangle(bx - bsz, by - bsz + 2, bsz, bsz);
                var b2  = new Rectangle(bx - bsz / 2, by - bsz + 2, bsz, bsz);
                using (var cb = new SolidBrush(Color.FromArgb(210, 10, 10, 18)))
                { g.FillEllipse(cb, b1); g.FillEllipse(cb, b2); }
                using (var cp = new Pen(Color.FromArgb(220, 220, 190, 50), 1.2f))
                { g.DrawEllipse(cp, b1); g.DrawEllipse(cp, b2); }
            }

            // Rank badge intentionally omitted
        }

        // ?? Mouse interaction ?????????????????????????????????????????????

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left)
            {
                // Ctrl+Left click on a node: toggle done ↔ nyi
                if ((ModifierKeys & Keys.Control) != 0)
                {
                    if (HitTestIconBar(new Point(e.X, e.Y)) >= 0) return;

                    var pt   = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
                    var node = HitTest(pt);
                    if (node == null) return;

                    var (currentStatus, currentNotes) =
                        QuerySpellStatusNotes?.Invoke(node.ActiveSpellId) ?? ("nyi", string.Empty);

                    var newStatus = currentStatus == "done" ? "nyi" : "done";
                    NodeStatusNotesUpdated?.Invoke(node, newStatus, currentNotes);
                    Invalidate();
                    return;
                }

                int idx = HitTestIconBar(new Point(e.X, e.Y));
                if (idx < 0)
                {
                    // Plain left-click on a choice node: show choice picker
                    var pt2  = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
                    var hit2 = HitTest(pt2);
                    if (hit2 != null && hit2.IsChoiceNode)
                        ShowChoiceMenu(hit2, e.Location);
                    return;
                }

                var entry = GetClassEntry(idx);
                if (entry == null) return;

                _selectedIconIndex = idx;
                Invalidate();

                ShowSpecMenu(entry, e.Location);
            }
            else if (e.Button == MouseButtons.Right)
            {
                // Don't intercept right-clicks on the icon bar
                if (HitTestIconBar(new Point(e.X, e.Y)) >= 0) return;

                var pt   = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
                var node = HitTest(pt);
                if (node == null) return;

                ShowNodeContextMenu(node, e.Location);
            }
        }

        private void ShowChoiceMenu(TalentNode node, Point location)
        {
            var cms = new ContextMenuStrip();
            ApplyDarkStyle(cms);

            string name0 = node.Name;
            string name1 = string.IsNullOrEmpty(node.AltName)
                ? $"Alt Spell ({node.AltSpellId})"
                : node.AltName;

            var item0 = new ToolStripMenuItem((node.SelectedChoiceIndex == 0 ? "\u2713 " : "   ") + name0);
            ApplyDarkStyle(item0);
            item0.Click += (_, _) => { node.SelectedChoiceIndex = 0; Invalidate(); };

            var item1 = new ToolStripMenuItem((node.SelectedChoiceIndex == 1 ? "\u2713 " : "   ") + name1);
            ApplyDarkStyle(item1);
            item1.Click += (_, _) => { node.SelectedChoiceIndex = 1; Invalidate(); };

            cms.Items.Add(item0);
            cms.Items.Add(item1);
            cms.Show(this, location);
        }

        private void ShowNodeContextMenu(TalentNode node, Point location)
        {
            var (currentStatus, currentNotes) =
                QuerySpellStatusNotes?.Invoke(node.ActiveSpellId) ?? ("nyi", string.Empty);

            var cms = new ContextMenuStrip();
            ApplyDarkStyle(cms);

            // Open in Spell Info
            var openItem = new ToolStripMenuItem("Open in Spell Info");
            ApplyDarkStyle(openItem);
            openItem.Enabled = node.ActiveSpellId > 0;
            openItem.Click  += (_, _) => NodeOpenInSpellInfo?.Invoke(node);
            cms.Items.Add(openItem);

            cms.Items.Add(new ToolStripSeparator());

            // Status sub-menu
            var statusItem = new ToolStripMenuItem("Set Status");
            ApplyDarkStyle(statusItem);
            foreach (var s in new[] { "done", "wip", "nyi", "unknown" })
            {
                var si = new ToolStripMenuItem(s)
                {
                    Checked   = string.Equals(currentStatus, s, StringComparison.OrdinalIgnoreCase),
                    BackColor = System.Drawing.Color.FromArgb(24, 24, 32),
                    ForeColor = System.Drawing.Color.WhiteSmoke,
                };
                var capS = s;
                si.Click += (_, _) => NodeStatusNotesUpdated?.Invoke(node, capS, currentNotes);
                statusItem.DropDownItems.Add(si);
            }
            cms.Items.Add(statusItem);

            // Set Notes
            var notesItem = new ToolStripMenuItem("Set Notes...");
            ApplyDarkStyle(notesItem);
            notesItem.Click += (_, _) =>
            {
                var newNotes = ShowNotesDialog(node.ActiveName, currentNotes);
                if (newNotes != null)
                    NodeStatusNotesUpdated?.Invoke(node, currentStatus, newNotes);
            };
            cms.Items.Add(notesItem);

            // FORGE SPELL – available for all NYI nodes
            if (currentStatus == "nyi" && node.ActiveSpellId > 0)
            {
                cms.Items.Add(new ToolStripSeparator());

                var implementItem = new ToolStripMenuItem("FORGE SPELL")
                {
                    BackColor = Color.FromArgb(24, 24, 32),
                    ForeColor = Color.FromArgb(255, 128, 0),
                    Font      = new Font(cms.Font, FontStyle.Bold),
                };
                implementItem.Click += (_, _) => ShowImplementDialog(node, currentNotes);
                cms.Items.Add(implementItem);
            }

            cms.Show(this, location);
        }

        // ── IMPLEMENT prompt ──────────────────────────────────────────────────

        private void ShowImplementDialog(TalentNode node, string currentNotes)
        {
            var prompt = BuildImplementPrompt(node, currentNotes);

            using var frm = new Form
            {
                Text            = $"FORGE SPELL – {node.ActiveName}",
                Width           = 780,
                Height          = 620,
                StartPosition   = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                BackColor       = Color.FromArgb(18, 18, 26),
                ForeColor       = Color.WhiteSmoke,
            };

            var tb = new TextBox
            {
                Multiline  = true,
                ScrollBars = ScrollBars.Both,
                Dock       = DockStyle.Fill,
                Text       = prompt,
                BackColor  = Color.FromArgb(26, 26, 36),
                ForeColor  = Color.WhiteSmoke,
                Font       = new Font("Consolas", 9f),
                WordWrap   = false,
                ReadOnly   = true,
            };

            var panel = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 40,
                BackColor = Color.FromArgb(14, 14, 20),
            };

            var copyBtn = new Button
            {
                Text      = "Copy to Clipboard",
                Width     = 150,
                Height    = 28,
                Left      = frm.ClientSize.Width - 170,
                Top       = 6,
                BackColor = Color.FromArgb(40, 40, 60),
                ForeColor = Color.FromArgb(100, 200, 255),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
            };
            copyBtn.Click += (_, _) =>
            {
                Clipboard.SetText(tb.Text);
                copyBtn.Text = "Copied!";
            };

            panel.Controls.Add(copyBtn);
            frm.Controls.AddRange(new Control[] { tb, panel });
            frm.ShowDialog();
        }

        private string BuildImplementPrompt(TalentNode node, string currentNotes)
        {
            var sb = new StringBuilder();
            DBC.DBC.SpellInfoStore.TryGetValue(node.ActiveSpellId, out var talentSpell);

            sb.AppendLine($"Implement {node.ActiveName}");
            sb.AppendLine();

            // ── TALENT ──────────────────────────────────────────────────────────
            sb.AppendLine("=== TALENT ===");
            if (talentSpell != null)
            {
                AppendSpellBlock(sb, talentSpell, "Talent");
            }
            else
            {
                sb.AppendLine($"Spell ID: {node.ActiveSpellId}  (not found in DBC)");
            }

            // ── Triggered spells directly referenced in DBC effects ──────────
            var dbcTriggered = new List<Spell.SpellInfo>();
            if (talentSpell != null)
            {
                foreach (var eff in talentSpell.SpellEffectInfoStore)
                {
                    var trigger = eff.EffectTriggerSpell;
                    if (trigger > 0 && trigger != node.ActiveSpellId &&
                        DBC.DBC.SpellInfoStore.TryGetValue(trigger, out var tSp))
                    {
                        if (!dbcTriggered.Any(x => x.ID == tSp.ID))
                            dbcTriggered.Add(tSp);
                    }
                }
            }

            // ── Sniff-matched related spells (same-name buffs / procs) ────────
            var sniffRelated = QueryImplementationSpells?.Invoke(node.ActiveSpellId)
                               ?? Array.Empty<Spell.SpellInfo>();

            // Merge DBC-triggered first, then sniff-matched (dedup by ID)
            var allRelated = dbcTriggered.ToList();
            foreach (var s in sniffRelated)
                if (!allRelated.Any(x => x.ID == s.ID) && s.ID != node.ActiveSpellId)
                    allRelated.Add(s);

            if (allRelated.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("=== RELATED SPELLS (buffs / procs / triggered / description-referenced) ===");
                foreach (var s in allRelated)
                {
                    sb.AppendLine();
                    bool isDbcTrigger = dbcTriggered.Any(x => x.ID == s.ID);
                    bool isSniff      = sniffRelated.Any(x => x.ID == s.ID);

                    // Try to determine if the spell name appears in the talent description
                    bool isDescRef = false;
                    if (talentSpell != null && !string.IsNullOrEmpty(s.Name))
                    {
                        var descText = string.Concat(talentSpell.Description, " ", talentSpell.Tooltip);
                        isDescRef = !isDbcTrigger &&
                                    descText.Contains(s.Name, StringComparison.OrdinalIgnoreCase);
                    }

                    // Detect summon-creature spells: they are sniff spells whose name does
                    // not appear in the description and have no DBC trigger link — they were
                    // pulled in transitively because a summon aura was already in the result.
                    bool isSummonCreatureCast = isSniff && !isDbcTrigger && !isDescRef;

                    var tags = string.Join(", ",
                        new[]
                        {
                            isDbcTrigger         ? "DBC Trigger"       : null,
                            isDescRef            ? "Desc Ref"          : null,
                            isSummonCreatureCast ? "Summoned Unit Cast" : null,
                            isSniff              ? "Sniff"             : null,
                        }
                        .Where(t => t != null));
                    AppendSpellBlock(sb, s, tags);

                    // For every SPELL_EFFECT_SUMMON in this spell, query the sniff for spells
                    // that the summoned creature was actually observed casting.  This surfaces
                    // IDs not present in DBC (e.g. "-Unknown-" entries) that the implementer
                    // needs to know about.
                    if (QuerySummonedCreatureCasts != null)
                    {
                        foreach (var eff in s.SpellEffectInfoStore)
                        {
                            if (eff.Effect != (int)Spell.SpellEffects.SPELL_EFFECT_SUMMON ||
                                eff.EffectMiscValueA == 0)
                                continue;

                            var observedCasts = QuerySummonedCreatureCasts(eff.EffectMiscValueA);
                            if (observedCasts.Count == 0) continue;

                            sb.AppendLine($"  --- Sniff: Creature {eff.EffectMiscValueA} observed casts ---");
                            foreach (var castId in observedCasts)
                            {
                                var castLabel = DBC.DBC.SpellInfoStore.TryGetValue(castId, out var castSpell)
                                    ? castSpell.NameAndSubname
                                    : "-Unknown- (not in DBC)";
                                sb.AppendLine($"    SpellID {castId}: {castLabel}");
                            }
                        }
                    }
                }
            }

            // ── Sniff evidence summary ────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("=== SNIFF EVIDENCE ===");
            sb.AppendLine("The following spell IDs were observed in the packet capture:");
            if (talentSpell != null)
                sb.AppendLine($"  - {node.ActiveSpellId}: {talentSpell.NameAndSubname}  [TALENT]");
            else
                sb.AppendLine($"  - {node.ActiveSpellId}: {node.ActiveName}  [TALENT]");
            foreach (var s in sniffRelated)
                sb.AppendLine($"  - {s.ID}: {s.NameAndSubname}  [RELATED]");

            if (!string.IsNullOrWhiteSpace(currentNotes))
            {
                sb.AppendLine();
                sb.AppendLine("=== NOTES ===");
                sb.AppendLine(currentNotes);
            }

            // ── Sniff database evidence (aggregated stats from imported captures) ──
            var sniffEvidence = Database.SniffPromptGenerator.BuildEvidenceBlock(
                node.ActiveSpellId,
                allRelated.AsReadOnly());
            if (sniffEvidence != null)
            {
                sb.AppendLine();
                sb.Append(sniffEvidence);
            }

            sb.AppendLine();
            sb.AppendLine("Provide me only new enums, scripts and SQL to handle the implementation.");

            return sb.ToString();
        }

        /// Formats a single <see cref="Spell.SpellInfo"/> into a dense, structured text block
        /// suitable for an AI implementation prompt.
        private static void AppendSpellBlock(StringBuilder sb, Spell.SpellInfo spell, string role)
        {
            var title = string.IsNullOrEmpty(role)
                ? $"--- {spell.NameAndSubname} (ID: {spell.ID}) ---"
                : $"--- {spell.NameAndSubname} (ID: {spell.ID})  [{role}] ---";
            sb.AppendLine(title);

            if (!string.IsNullOrEmpty(spell.Description))
                sb.AppendLine($"  Description : {spell.Description}");
            if (!string.IsNullOrEmpty(spell.Tooltip))
                sb.AppendLine($"  Tooltip     : {spell.Tooltip}");

            sb.AppendLine($"  SpellFamily : {(Spell.SpellFamilyNames)spell.SpellFamilyName} ({spell.SpellFamilyName})");
            sb.AppendLine($"  ClassMask   : [0] 0x{spell.SpellClassMask[0]:X8}  [1] 0x{spell.SpellClassMask[1]:X8}" +
                          $"  [2] 0x{spell.SpellClassMask[2]:X8}  [3] 0x{spell.SpellClassMask[3]:X8}");

            if (spell.DurationEntry != null && spell.DurationEntry.Duration != 0)
                sb.AppendLine($"  Duration    : {spell.DurationEntry.Duration} ms");
            if (spell.RecoveryTime > 0)
                sb.AppendLine($"  Cooldown    : {spell.RecoveryTime} ms");
            else if (spell.CategoryRecoveryTime > 0)
                sb.AppendLine($"  CDCategory  : {spell.CategoryRecoveryTime} ms");
            if (spell.ProcFlags != 0 || spell.ProcFlagsEx != 0)
                sb.AppendLine($"  ProcFlags   : 0x{spell.ProcFlags:X8}  flagEx 0x{spell.ProcFlagsEx:X8}  " +
                              $"chance {spell.ProcChance}%  charges {spell.ProcCharges}  cd {spell.ProcCooldown} ms");
            if (Math.Abs(spell.BaseProcRate) > 1e-5f)
                sb.AppendLine($"  RPPM        : {spell.BaseProcRate:F4} (flags 0x{spell.ProcsPerMinuteFlags:X2})");

            // Effects
            foreach (var eff in spell.SpellEffectInfoStore.OrderBy(e => e.EffectIndex))
            {
                var effectName = Enum.GetName((Spell.SpellEffects)eff.Effect) ?? $"{eff.Effect}";
                sb.AppendLine($"  Effect [{eff.EffectIndex}] : {effectName} ({eff.Effect})");

                if (eff.EffectAura != 0)
                {
                    var auraName = Enum.GetName((Spell.AuraType)eff.EffectAura) ?? $"{eff.EffectAura}";
                    sb.AppendLine($"    AuraType  : {auraName} ({eff.EffectAura})");
                    if (eff.EffectMiscValueA != 0)
                        sb.AppendLine($"    MiscA     : {eff.EffectMiscValueA}");
                    if (eff.EffectMiscValueB != 0)
                        sb.AppendLine($"    MiscB     : {eff.EffectMiscValueB}");
                    if (eff.EffectAuraPeriod != 0)
                        sb.AppendLine($"    Period    : {eff.EffectAuraPeriod} ms");
                }
                else if (eff.Effect == (int)Spell.SpellEffects.SPELL_EFFECT_SUMMON)
                {
                    // For summon effects MiscA is always the creature/NPC entry ID — label it
                    // explicitly so the implementer knows what creature is being spawned.
                    sb.AppendLine($"    NPC Entry : {eff.EffectMiscValueA}  (creature summoned)");
                    if (eff.EffectMiscValueB != 0)
                        sb.AppendLine($"    MiscB     : {eff.EffectMiscValueB}");
                }
                else
                {
                    if (eff.EffectMiscValueA != 0)
                        sb.AppendLine($"    MiscA     : {eff.EffectMiscValueA}");
                    if (eff.EffectMiscValueB != 0)
                        sb.AppendLine($"    MiscB     : {eff.EffectMiscValueB}");
                }

                sb.AppendLine($"    BasePoints: {eff.EffectBasePoints:F2}");

                if (eff.EffectBonusCoefficient > 1e-5f)
                    sb.AppendLine($"    SpCoef    : {eff.EffectBonusCoefficient:F4}");
                if (eff.BonusCoefficientFromAP > 1e-5f)
                    sb.AppendLine($"    APCoef    : {eff.BonusCoefficientFromAP:F4}");

                if (eff.EffectTriggerSpell > 0)
                {
                    var tName = DBC.DBC.SpellInfoStore.TryGetValue(eff.EffectTriggerSpell, out var tSp)
                        ? tSp.Name : "unknown";
                    sb.AppendLine($"    Trigger   : {eff.EffectTriggerSpell} ({tName})");
                }

                var tA = Enum.GetName((Spell.Targets)eff.SpellEffect.ImplicitTarget[0]) ?? $"{eff.SpellEffect.ImplicitTarget[0]}";
                var tB = Enum.GetName((Spell.Targets)eff.SpellEffect.ImplicitTarget[1]) ?? $"{eff.SpellEffect.ImplicitTarget[1]}";
                sb.AppendLine($"    Targets   : A={tA}  B={tB}");
            }
        }

        private string? ShowNotesDialog(string nodeName, string currentNotes)
        {
            using var frm = new Form
            {
                Text            = $"Notes – {nodeName}",
                Width           = 420,
                Height          = 190,
                StartPosition   = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox     = false,
                MinimizeBox     = false,
                BackColor       = System.Drawing.Color.FromArgb(24, 24, 32),
                ForeColor       = System.Drawing.Color.WhiteSmoke,
            };

            var tb = new TextBox
            {
                Multiline = true,
                Dock      = DockStyle.Fill,
                Text      = currentNotes,
                BackColor = System.Drawing.Color.FromArgb(32, 32, 44),
                ForeColor = System.Drawing.Color.WhiteSmoke,
            };

            var panel = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 36,
                BackColor = System.Drawing.Color.FromArgb(18, 18, 26),
            };

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Width        = 80,
                Left         = frm.ClientSize.Width - 182,
                Top          = 6,
                BackColor    = System.Drawing.Color.FromArgb(40, 40, 55),
                ForeColor    = System.Drawing.Color.WhiteSmoke,
                FlatStyle    = FlatStyle.Flat,
            };

            var cancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width        = 80,
                Left         = frm.ClientSize.Width - 92,
                Top          = 6,
                BackColor    = System.Drawing.Color.FromArgb(40, 40, 55),
                ForeColor    = System.Drawing.Color.WhiteSmoke,
                FlatStyle    = FlatStyle.Flat,
            };

            panel.Controls.AddRange(new Control[] { ok, cancel });
            frm.Controls.AddRange(new Control[] { tb, panel });
            frm.AcceptButton = ok;
            frm.CancelButton = cancel;

            return frm.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }

        private void ShowSpecMenu(ClassEntry entry, Point menuLocation)
        {
            var cms = new ContextMenuStrip();
            ApplyDarkStyle(cms);

            foreach (var spec in entry.Specs)
            {
                var specItem = new ToolStripMenuItem(spec.Name);
                ApplyDarkStyle(specItem);

                foreach (var hero in spec.HeroTalents)
                {
                    var heroItem   = new ToolStripMenuItem(hero);
                    ApplyDarkStyle(heroItem);
                    var capClass   = entry.Name;
                    var capSpec    = spec.Name;
                    var capHero    = hero;
                    heroItem.Click += (_, _) =>
                    {
                        _selectedSpec = capSpec;
                        _selectedHero = capHero;
                        ClassSpecHeroSelected?.Invoke(capClass, capSpec, capHero);
                        Invalidate();
                    };
                    specItem.DropDownItems.Add(heroItem);
                }

                cms.Items.Add(specItem);
            }

            cms.Show(this, menuLocation);
        }

        private static void ApplyDarkStyle(ToolStrip ts)
        {
            ts.BackColor = Color.FromArgb(24, 24, 32);
            ts.ForeColor = Color.WhiteSmoke;
            ts.Renderer  = new ToolStripProfessionalRenderer(new DarkColorTable());
        }

        private static void ApplyDarkStyle(ToolStripMenuItem item)
        {
            item.BackColor = Color.FromArgb(24, 24, 32);
            item.ForeColor = Color.WhiteSmoke;
        }

        // Minimal dark colour table for the context menus
        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected          => Color.FromArgb(70, 60, 20);
            public override Color MenuItemBorder            => Color.FromArgb(160, 140, 40);
            public override Color MenuBorder                => Color.FromArgb(60, 60, 70);
            public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 32);
            public override Color ImageMarginGradientBegin  => Color.FromArgb(30, 30, 38);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 38);
            public override Color ImageMarginGradientEnd    => Color.FromArgb(30, 30, 38);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(70, 60, 20);
            public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(70, 60, 20);
            public override Color MenuStripGradientBegin    => Color.FromArgb(24, 24, 32);
            public override Color MenuStripGradientEnd      => Color.FromArgb(24, 24, 32);
            public override Color SeparatorDark             => Color.FromArgb(70, 70, 80);
            public override Color SeparatorLight            => Color.FromArgb(50, 50, 60);
        }

        // ── Custom WoW-style tooltip ───────────────────────────────────────────

        private enum TooltipRowKind { Normal, Desc, Sep, Gap }

        private sealed record TooltipRow(
            string Left, string Right,
            Color LeftColor, Color RightColor,
            Font Font, TooltipRowKind Kind);

        private void PaintNodeTooltip(Graphics g)
        {
            if (_hovered == null) return;

            var node = _hovered;
            DBC.DBC.SpellInfoStore.TryGetValue(node.ActiveSpellId, out var spell);
            var (status, notes) = QuerySpellStatusNotes?.Invoke(node.ActiveSpellId) ?? ("nyi", string.Empty);

            const int Pad  = 10;
            const int TipW = 296;
            int innerW = TipW - Pad * 2;

            using var nameFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var grayFont = new Font("Segoe UI",  9f, FontStyle.Regular);
            using var infoFont = new Font("Segoe UI",  9f, FontStyle.Regular);
            using var descFont = new Font("Segoe UI",  9f, FontStyle.Italic);
            using var noteFont = new Font("Segoe UI",  8.5f, FontStyle.Regular);

            var rows = new List<TooltipRow>();

            // ── Header ────────────────────────────────────────────────────
            rows.Add(new TooltipRow(node.ActiveName, string.Empty,
                Color.FromArgb(255, 255, 210, 30), Color.Empty, nameFont, TooltipRowKind.Normal));
            string subTitle = node.IsChoiceNode
                ? $"Talent  (Choice {node.SelectedChoiceIndex + 1} of 2)"
                : "Talent";
            rows.Add(new TooltipRow(subTitle, string.Empty,
                Color.FromArgb(160, 160, 170), Color.Empty, grayFont, TooltipRowKind.Normal));

            if (spell != null)
            {
                // ── Cost | Range ──────────────────────────────────────────
                string costStr  = string.Empty;
                string rangeStr = string.Empty;

                foreach (var power in spell.Powers)
                {
                    if (power.ManaCost > 0 || power.PowerCostPct > 0)
                    {
                        string pName = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                            .ToTitleCase(((Spell.Powers)power.PowerType)
                                .ToString().Replace("POWER_", string.Empty, StringComparison.Ordinal)
                                .ToLowerInvariant());
                        costStr = power.ManaCost > 0
                            ? $"{power.ManaCost} {pName}"
                            : $"{power.PowerCostPct}% {pName}";
                        break;
                    }
                }

                if (spell.Range != null && !string.IsNullOrEmpty(spell.Range.DisplayName))
                    rangeStr = spell.Range.DisplayName;

                if (!string.IsNullOrEmpty(costStr) || !string.IsNullOrEmpty(rangeStr))
                    rows.Add(new TooltipRow(costStr, rangeStr,
                        Color.WhiteSmoke, Color.WhiteSmoke, infoFont, TooltipRowKind.Normal));

                // ── Cast | Cooldown ───────────────────────────────────────
                string castStr = "Instant";
                if (spell.CastingTimeIndex != 0 &&
                    DBC.DBC.SpellCastTimes.TryGetValue(spell.CastingTimeIndex, out var ct) &&
                    ct.Base > 0)
                    castStr = $"{ct.Base / 1000.0:F1} sec cast";

                int cdMs = spell.RecoveryTime > 0 ? spell.RecoveryTime : spell.CategoryRecoveryTime;
                string cdStr = cdMs > 0 ? $"{cdMs / 1000.0:F0} sec cooldown" : string.Empty;

                rows.Add(new TooltipRow(castStr, cdStr,
                    Color.WhiteSmoke, Color.WhiteSmoke, infoFont, TooltipRowKind.Normal));

                // ── Description ───────────────────────────────────────────
                var descText = !string.IsNullOrEmpty(spell.Description)
                    ? spell.Description : spell.Tooltip;
                if (!string.IsNullOrEmpty(descText))
                {
                    rows.Add(new TooltipRow(string.Empty, string.Empty,
                        Color.Empty, Color.Empty, infoFont, TooltipRowKind.Sep));
                    rows.Add(new TooltipRow(descText, string.Empty,
                        Color.FromArgb(255, 255, 175, 30), Color.Empty, descFont, TooltipRowKind.Desc));
                }
            }

            // ── Rank ──────────────────────────────────────────────────────
            if (node.MaxRank > 1)
            {
                rows.Add(new TooltipRow(string.Empty, string.Empty,
                    Color.Empty, Color.Empty, noteFont, TooltipRowKind.Gap));
                rows.Add(new TooltipRow($"Rank {node.CurrentRank}/{node.MaxRank}", string.Empty,
                    Color.FromArgb(155, 155, 165), Color.Empty, noteFont, TooltipRowKind.Normal));
            }

            // ── Status / Notes ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(status) && status != "unknown")
            {
                var statusColor = status switch
                {
                    "done" => Color.FromArgb(120, 220, 120),
                    "wip"  => Color.FromArgb(255, 200,  80),
                    "nyi"  => Color.FromArgb(220, 100, 100),
                    _      => Color.FromArgb(160, 160, 170),
                };
                rows.Add(new TooltipRow(string.Empty, string.Empty,
                    Color.Empty, Color.Empty, noteFont, TooltipRowKind.Sep));
                rows.Add(new TooltipRow($"Status: {status}", string.Empty,
                    statusColor, Color.Empty, noteFont, TooltipRowKind.Normal));
            }
            if (!string.IsNullOrEmpty(notes))
            {
                rows.Add(new TooltipRow(string.Empty, string.Empty,
                    Color.Empty, Color.Empty, noteFont, TooltipRowKind.Gap));
                rows.Add(new TooltipRow("NOTES:", string.Empty,
                    Color.FromArgb(180, 180, 190), Color.Empty, noteFont, TooltipRowKind.Normal));
                rows.Add(new TooltipRow(notes, string.Empty,
                    Color.FromArgb(160, 200, 240), Color.Empty, noteFont, TooltipRowKind.Desc));
            }

            // ── Measure total height ──────────────────────────────────────
            var rowHeights = new float[rows.Count];
            float totalH   = Pad * 2;
            for (int i = 0; i < rows.Count; i++)
            {
                rowHeights[i] = rows[i].Kind switch
                {
                    TooltipRowKind.Gap  => 5f,
                    TooltipRowKind.Sep  => 10f,
                    TooltipRowKind.Desc => g.MeasureString(rows[i].Left, rows[i].Font, innerW).Height + 2f,
                    _                   => rows[i].Font.GetHeight(g) + 3f,
                };
                totalH += rowHeights[i];
            }

            // ── Position ──────────────────────────────────────────────────
            int tx = _mousePos.X + 16;
            int ty = _mousePos.Y + 16;
            if (tx + TipW    > ClientSize.Width)   tx = _mousePos.X - TipW - 4;
            if (ty + totalH  > ClientSize.Height)  ty = _mousePos.Y - (int)totalH - 4;
            tx = Math.Max(2, tx);
            ty = Math.Max(2, ty);

            // ── Background ────────────────────────────────────────────────
            var bg = new Rectangle(tx, ty, TipW, (int)totalH);

            using (var bgBrush = new SolidBrush(Color.FromArgb(238, 8, 8, 14)))
                g.FillRectangle(bgBrush, bg);

            // Outer gold border
            using (var outerPen = new Pen(Color.FromArgb(215, 180, 140, 28), 1.5f))
                g.DrawRectangle(outerPen, bg);

            // Inner dark inset border
            using (var innerPen = new Pen(Color.FromArgb(90, 90, 60, 10), 1f))
                g.DrawRectangle(innerPen,
                    new Rectangle(bg.X + 2, bg.Y + 2, bg.Width - 4, bg.Height - 4));

            // ── Draw rows ─────────────────────────────────────────────────
            float y = ty + Pad;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float rh = rowHeights[i];

                switch (row.Kind)
                {
                    case TooltipRowKind.Gap:
                        break;

                    case TooltipRowKind.Sep:
                    {
                        float sepY = y + rh / 2f;
                        using var sp = new Pen(Color.FromArgb(130, 180, 140, 20), 1f);
                        g.DrawLine(sp, tx + Pad, sepY, tx + TipW - Pad, sepY);
                        break;
                    }

                    case TooltipRowKind.Desc:
                    {
                        using var b = new SolidBrush(row.LeftColor);
                        g.DrawString(row.Left, row.Font, b,
                            new RectangleF(tx + Pad, y, innerW, rh + 60));
                        break;
                    }

                    default: // Normal
                    {
                        using var lb = new SolidBrush(row.LeftColor);
                        g.DrawString(row.Left, row.Font, lb, tx + Pad, y);
                        if (!string.IsNullOrEmpty(row.Right))
                        {
                            var rc = row.RightColor.A > 0 ? row.RightColor : row.LeftColor;
                            var rsz = g.MeasureString(row.Right, row.Font);
                            using var rb = new SolidBrush(rc);
                            g.DrawString(row.Right, row.Font, rb,
                                tx + TipW - Pad - rsz.Width, y);
                        }
                        break;
                    }
                }
                y += rh;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Icon bar is fixed – use raw screen coordinates.
            int newIconHover = HitTestIconBar(new Point(e.X, e.Y));
            if (newIconHover != _iconBarHoverIndex)
            {
                _iconBarHoverIndex = newIconHover;
                if (newIconHover >= 0 && _classIconNames != null)
                    _tip.SetToolTip(this, _classIconNames[newIconHover]);
                else
                    _tip.SetToolTip(this, string.Empty);
                Invalidate();
                return;   // don't process tree hover while over the bar
            }

            // Tree nodes use scroll-adjusted coordinates.
            _mousePos = new Point(e.X, e.Y);
            var pt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            var hit = HitTest(pt);
            if (hit != _hovered)
            {
                _hovered = hit;
                _tip.SetToolTip(this, string.Empty);
            }
            Invalidate();
        }

        private int HitTestIconBar(Point pt)
        {
            if (_classIcons == null || _classIcons.Length == 0)
                return -1;
            if (pt.Y < 0 || pt.Y >= IconBarHeight)
                return -1;

            int n        = _classIcons.Length;
            int barW     = ClientSize.Width;
            int totalIconW = n * IconSize + (n - 1) * IconPad;
            int startX   = Math.Max(TreeGap, barW / 2 - totalIconW / 2);
            int iconY    = (IconBarHeight - IconSize) / 2;

            for (int i = 0; i < n; i++)
            {
                int ix = startX + i * (IconSize + IconPad);
                if (pt.X >= ix && pt.X < ix + IconSize &&
                    pt.Y >= iconY && pt.Y < iconY + IconSize)
                    return i;
            }
            return -1;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool needRepaint = _hovered != null || _iconBarHoverIndex >= 0;
            _hovered = null;
            _iconBarHoverIndex = -1;
            if (needRepaint) Invalidate();
        }

        private TalentNode? HitTest(Point pt)
        {
            int ox = _treeGap;
            foreach (var tree in _trees)
            {
                foreach (var node in tree.Nodes)
                {
                    var (cx, cy) = NodeCenter(node, ox);
                    int half = (node.IsGate ? GateDiameter : NodeDiameter) / 2 + 2;
                    if (Math.Abs(pt.X - cx) <= half && Math.Abs(pt.Y - cy) <= half)
                        return node;
                }
                ox += TreeWidth(tree) + _treeGap;
            }
            return null;
        }
    }
}
