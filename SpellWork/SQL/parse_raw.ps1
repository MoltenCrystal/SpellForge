<#
.SYNOPSIS
    Converts raw_CLASS_SPEC_HERO.txt Wowhead talent-tree HTML files into SQL files,
    one parsed_CLASS_SPEC_HERO.sql per raw file.

.DESCRIPTION
    Tables populated
    ----------------
    talent_tree_nodes       - one row per unique cell_id in each tree section
    talent_tree_connections - one row per arrow between two cells
    talent_spells           - one row per unique spell_id discovered

    Each output SQL file first DELETEs the existing rows for that
    class/spec/hero combination, then re-inserts fresh data.
    talent_spells uses INSERT ... ON DUPLICATE KEY UPDATE to handle
    spells that are shared across multiple specs without raising warnings.

    Icons are downloaded to:  <ScriptDir>\talentIcons\<class>\<icon>.jpg
    The SpellWork app looks for them at:  <AppBaseDir>\SQL\talentIcons\<class>\

.EXAMPLE
    # Process all raw_*.txt files in the same folder
    .\parse_raw.ps1

.EXAMPLE
    # Process a specific file
    .\parse_raw.ps1 -Files .\raw_warrior_arms_colossus.txt

.NOTES
    HTML anatomy expected
    ---------------------
    Tree sections are delimited by:
      <div ... data-tree-type="class|spec|hero" ...>

    Talent nodes look like:
      <a class="dragonflight-talent-tree-talent"
         data-row="2" data-column="12" data-cell="30" data-talent-type="1"
         href="https://www.wowhead.com/spell=6343/thunder-clap"
         aria-label="Thunder Clap" ...>

    Choice nodes additionally carry:
      data-choice-href0=".../spell=3411/intervene"
      data-choice-href1=".../spell=1244088/interpose"

    Rank text comes from the sibling points div:
      <div class="dragonflight-talent-tree-talent-points"
           data-cell="30" ...>0/1</div>   <- "0/1" -> max_rank = 1
                                            ""    -> max_rank = 1 (already full)

    Connections look like:
      <div class="dragonflight-talent-tree-connection"
           data-from-cell="13" data-to-cell="30" ...>
#>

[CmdletBinding()]
param(
    [string[]] $Files
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir        = Split-Path -Parent $MyInvocation.MyCommand.Path
# Spell icons: SQL\talentIcons\<class>\<spell_icon>.jpg
$IconBaseDir      = Join-Path $ScriptDir 'talentIcons'
# Class icons: SQL\classIcons\class_<classname>.jpg
$ClassIconBaseDir = Join-Path $ScriptDir 'classIcons'

# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------

function Get-Attr([string]$Tag, [string]$Name, [string]$Default = '') {
    if ($Tag -match "(?i)\b$([regex]::Escape($Name))=""([^""]*)""") {
        return $Matches[1]
    }
    return $Default
}

function Get-IntAttr([string]$Tag, [string]$Name, [int]$Default = -1) {
    $val = Get-Attr $Tag $Name
    $parsed = 0
    if ([int]::TryParse($val, [ref]$parsed)) { return $parsed }
    return $Default
}

function Get-SpellIdFromHref([string]$Href) {
    if ($Href -match '/spell=(\d+)/') { return [int]$Matches[1] }
    return 0
}

function Escape-Sql([string]$s) {
    return $s.Replace("'", "''")
}

function Get-NameFromHref([string]$Href) {
    if ($Href -match '/spell=\d+/([^/?&"#]+)') {
        return (Get-Culture).TextInfo.ToTitleCase($Matches[1].Replace('-', ' '))
    }
    return ''
}

function Extract-AltIconMap([string]$Section) {
    # Returns hashtable: cell_id -> second icon filename, for choice nodes only.
    $map = @{}
    $rx = [regex]'(?is)(<a[^>]+class="[^"]*dragonflight-talent-tree-talent[^"]*"[^>]+>)(.*?)</a>'
    foreach ($m in $rx.Matches($Section)) {
        $anchorTag = $m.Groups[1].Value
        $cellId    = Get-IntAttr $anchorTag 'data-cell'
        if ($cellId -lt 0 -or $map.ContainsKey($cellId)) { continue }
        if ((Get-Attr $anchorTag 'data-choice-href0') -eq '') { continue }
        $innerHtml = $m.Groups[2].Value
        $bgRx  = [regex]'url\((?:&quot;|''|"|)?(?:https?:)?//wow\.zamimg\.com/images/wow/icons/large/([^&''"\.)\s]+\.jpg)(?:&quot;|''|")?'
        $allBg = $bgRx.Matches($innerHtml)
        if ($allBg.Count -ge 2) {
            $map[$cellId] = $allBg[1].Groups[1].Value
        }
    }
    return $map
}

function Extract-IconMap([string]$Section) {
    # Returns hashtable: cell_id -> icon filename (e.g. "ability_warrior_charge.jpg")
    # Tries four patterns in priority order so we catch every Wowhead HTML variant:
    #   1. data-icon attribute on the anchor tag            data-icon="ability_warrior_charge"
    #   2. zamimg large-icon URL in CSS background-image    url(//wow.zamimg.com/.../icon.jpg)
    #   3. <img src> containing a zamimg large-icon URL
    #   4. data-icon attribute on any child element
    $map = @{}
    $rx = [regex]'(?is)(<a[^>]+class="[^"]*dragonflight-talent-tree-talent[^"]*"[^>]+>)(.*?)</a>'
    foreach ($m in $rx.Matches($Section)) {
        $cellId = Get-IntAttr $m.Groups[1].Value 'data-cell'
        if ($cellId -lt 0 -or $map.ContainsKey($cellId)) { continue }

        $anchorTag = $m.Groups[1].Value
        $innerHtml = $m.Groups[2].Value

        # 1. data-icon on the anchor itself (most reliable)
        $iconAttr = Get-Attr $anchorTag 'data-icon'
        if ($iconAttr -ne '') {
            $map[$cellId] = if ($iconAttr.EndsWith('.jpg')) { $iconAttr } else { "$iconAttr.jpg" }
            continue
        }

        # 2. CSS background-image url � handles &quot;, ", ', no-quote, http/https/protocol-relative
        if ($innerHtml -match 'url\((?:&quot;|''|"|)?(?:https?:)?//wow\.zamimg\.com/images/wow/icons/large/([^&''"\.\)\s]+\.jpg)(?:&quot;|''|")?') {
            $map[$cellId] = $Matches[1]
            continue
        }

        # 3. <img src> with zamimg large icon
        if ($innerHtml -match '<img[^>]+src="(?:https?:)?//wow\.zamimg\.com/images/wow/icons/large/([^"?]+\.jpg)"') {
            $map[$cellId] = $Matches[1]
            continue
        }

        # 4. data-icon on any child element
        if ($innerHtml -match '(?i)\bdata-icon="([^"]+)"') {
            $v = $Matches[1]
            $map[$cellId] = if ($v.EndsWith('.jpg')) { $v } else { "$v.jpg" }
        }
    }
    return $map
}

function Download-Icons([object[]]$Pending) {
    if ($Pending.Count -eq 0) { return }
    $client = [System.Net.WebClient]::new()
    $client.Headers.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36')
    $downloaded = 0; $skipped = 0; $failed = 0
    foreach ($item in $Pending) {
        if (Test-Path $item.Dest) { $skipped++; continue }
        try {
            $dir = Split-Path -Parent $item.Dest
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
            $client.DownloadFile($item.Url, $item.Dest)
            $downloaded++
        } catch {
            Write-Warning "  Icon download failed: $($item.Url) -> $_"
            $failed++
        }
    }
    $client.Dispose()
    Write-Host ("  Icons: {0} downloaded, {1} already present{2}" -f `
        $downloaded, $skipped, $(if ($failed -gt 0) { ", $failed failed" } else { '' }))
}

function Download-ClassIcons {
    # Maps the zamimg classicon filename to our local class_<name>.jpg convention.
    # local name is class_ + lowercase class name with spaces replaced by underscores.
    $icons = @(
        @{ Src = 'classicon_deathknight.jpg';  Dest = 'class_death_knight.jpg' },
        @{ Src = 'classicon_demonhunter.jpg';  Dest = 'class_demon_hunter.jpg' },
        @{ Src = 'classicon_druid.jpg';        Dest = 'class_druid.jpg'        },
        @{ Src = 'classicon_evoker.jpg';       Dest = 'class_evoker.jpg'       },
        @{ Src = 'classicon_hunter.jpg';       Dest = 'class_hunter.jpg'       },
        @{ Src = 'classicon_mage.jpg';         Dest = 'class_mage.jpg'         },
        @{ Src = 'classicon_monk.jpg';         Dest = 'class_monk.jpg'         },
        @{ Src = 'classicon_paladin.jpg';      Dest = 'class_paladin.jpg'      },
        @{ Src = 'classicon_priest.jpg';       Dest = 'class_priest.jpg'       },
        @{ Src = 'classicon_rogue.jpg';        Dest = 'class_rogue.jpg'        },
        @{ Src = 'classicon_shaman.jpg';       Dest = 'class_shaman.jpg'       },
        @{ Src = 'classicon_warlock.jpg';      Dest = 'class_warlock.jpg'      },
        @{ Src = 'classicon_warrior.jpg';      Dest = 'class_warrior.jpg'      }
    )

    if (-not (Test-Path $script:ClassIconBaseDir)) {
        New-Item -ItemType Directory -Path $script:ClassIconBaseDir -Force | Out-Null
    }

    $client = [System.Net.WebClient]::new()
    $client.Headers.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36')
    $downloaded = 0; $skipped = 0; $failed = 0
    foreach ($icon in $icons) {
        $dest = Join-Path $script:ClassIconBaseDir $icon.Dest
        if (Test-Path $dest) { $skipped++; continue }
        try {
            $url = "https://wow.zamimg.com/images/wow/icons/large/$($icon.Src)"
            $client.DownloadFile($url, $dest)
            $downloaded++
        } catch {
            Write-Warning "  Class icon download failed: $($icon.Src) -> $_"
            $failed++
        }
    }
    $client.Dispose()
    Write-Host ("  Class icons: {0} downloaded, {1} already present{2}" -f `
        $downloaded, $skipped, $(if ($failed -gt 0) { ", $failed failed" } else { '' }))
}

# ---------------------------------------------------------------------------
# Filename component resolution
# ---------------------------------------------------------------------------
# All known class and spec names, lowercase with underscores, sorted longest-
# first so that e.g. "death_knight" is tested before "druid", and
# "beast_mastery" before "beast".

$script:KnownClasses = @(
    'death_knight', 'demon_hunter',
    'warrior', 'warlock', 'shaman', 'rogue', 'priest', 'paladin',
    'monk', 'mage', 'hunter', 'evoker', 'druid'
) | Sort-Object { $_.Length } -Descending

$script:KnownSpecs = @(
    'beast_mastery', 'marksmanship', 'windwalker', 'brewmaster', 'mistweaver',
    'devastation', 'preservation', 'augmentation', 'assassination', 'demonology',
    'destruction', 'retribution', 'discipline', 'elemental', 'enhancement',
    'restoration', 'protection', 'affliction', 'subtlety', 'balance', 'guardian',
    'feral', 'havoc', 'vengeance', 'outlaw', 'blood', 'frost', 'unholy',
    'arcane', 'fire', 'fury', 'holy', 'arms', 'shadow'
) | Sort-Object { $_.Length } -Descending

<#
.SYNOPSIS
    Splits a raw filename stem (e.g. "warrior_protection_mountain_thane") into
    class, spec and hero components by matching against the known-name lists.
    Returns $null when no match is found.
#>
function Parse-FilenameComponents([string]$Stem) {
    foreach ($cls in $script:KnownClasses) {
        if (-not $Stem.StartsWith($cls + '_')) { continue }
        $rest = $Stem.Substring($cls.Length + 1)
        foreach ($spec in $script:KnownSpecs) {
            if (-not ($rest -eq $spec -or $rest.StartsWith($spec + '_'))) { continue }
            $hero = if ($rest -eq $spec) { '' } else { $rest.Substring($spec.Length + 1) }
            return [PSCustomObject]@{ Class = $cls; Spec = $spec; Hero = $hero }
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Per-file parser
# ---------------------------------------------------------------------------

function Parse-File([string]$FilePath) {
$filename = Split-Path -Leaf $FilePath

if ($filename -notmatch '^raw_(.+)\.txt$') {
    Write-Warning "Skipping $filename : filename does not start with raw_"
    return $null
}
$stem = $Matches[1].ToLower()

$parts = Parse-FilenameComponents $stem
if ($null -eq $parts) {
    Write-Warning ("Skipping $filename : could not identify class/spec/hero. " +
        "Ensure the class and spec match the known-name lists in this script.")
    return $null
}

# TitleCase each underscore-separated word: mountain_thane -> Mountain Thane
$tc        = (Get-Culture).TextInfo
$className = $tc.ToTitleCase($parts.Class.Replace('_', ' '))
$specName  = $tc.ToTitleCase($parts.Spec.Replace('_', ' '))
$heroName  = $tc.ToTitleCase($parts.Hero.Replace('_', ' '))

    $html = Get-Content -Path $FilePath -Raw -Encoding UTF8

    # ---- Locate tree-section start positions --------------------------------
    # Each section begins at a div carrying data-tree-type="class|spec|hero".
    $sectionStartRx = [regex]'(?i)<div[^>]+class="[^"]*dragonflight-talent-trees-tree[^"]*"[^>]+>'
    $sectionMatches = $sectionStartRx.Matches($html)

    if ($sectionMatches.Count -eq 0) {
        Write-Warning "No tree sections found in $filename"
        return $null
    }

    # Build list of (Offset, TreeType) pairs
    $sections = @()
    foreach ($sm in $sectionMatches) {
        if ($sm.Value -match '(?i)\bdata-tree-type="([^"]+)"') {
            $sections += [PSCustomObject]@{
                Offset   = $sm.Index
                TreeType = $Matches[1].ToLower()
            }
        }
    }

    $nodesRows       = [System.Collections.Generic.List[string]]::new()
    $connectionsRows = [System.Collections.Generic.List[string]]::new()
    $spellsRows      = [System.Collections.Generic.List[string]]::new()
    $seenSpellIds    = [System.Collections.Generic.HashSet[int]]::new()
    $pendingIcons    = @{}  # destPath -> url

    $talentAnchorRx  = [regex]'(?i)<a[^>]*class="[^"]*dragonflight-talent-tree-talent[^"]*"[^>]*>'
    $pointsDivRx     = [regex]'(?i)<div[^>]+class="[^"]*dragonflight-talent-tree-talent-points[^"]*"[^>]+data-cell="(\d+)"[^>]*>([^<]*)</div>'
    $connectionDivRx = [regex]'(?i)<div[^>]+class="[^"]*dragonflight-talent-tree-connection[^"]*"[^>]+>'

    for ($i = 0; $i -lt $sections.Count; $i++) {
        $treeType = $sections[$i].TreeType
        $start    = $sections[$i].Offset
        $end      = if ($i + 1 -lt $sections.Count) { $sections[$i + 1].Offset } else { $html.Length }
        $section  = $html.Substring($start, $end - $start)

        # hero_name is only meaningful for the hero tree
        $hName = if ($treeType -eq 'hero') { $heroName } else { '' }

        # ---- Build max_rank map from points divs ----------------------------
        $maxRankMap = @{}
        foreach ($pm in $pointsDivRx.Matches($section)) {
            $cellId = [int]$pm.Groups[1].Value
            $text   = $pm.Groups[2].Value.Trim()
            if ($text -match '/') {
                $maxRankMap[$cellId] = [int]($text -split '/')[1]
            } else {
                $maxRankMap[$cellId] = 1   # full / missing text -> rank 1
            }
        }

        # ---- Build icon map (cell_id -> filename) from anchor content --------
        $iconMap    = Extract-IconMap    $section
        $altIconMap = Extract-AltIconMap $section
        Write-Verbose ("  [{0}] icon map: {1} entries, alt: {2}" -f $treeType, $iconMap.Count, $altIconMap.Count)

        # ---- Talent nodes ---------------------------------------------------
        $seenCells = [System.Collections.Generic.HashSet[int]]::new()

        foreach ($anchor in $talentAnchorRx.Matches($section)) {
            $tag = $anchor.Value

            $row      = Get-IntAttr $tag 'data-row'
            $col      = Get-IntAttr $tag 'data-column'
            $cellId   = Get-IntAttr $tag 'data-cell'
            $nodeName = Get-Attr    $tag 'aria-label'
            $href     = Get-Attr    $tag 'href'
            $choice0  = Get-Attr    $tag 'data-choice-href0'
            $choice1  = Get-Attr    $tag 'data-choice-href1'

            if ($cellId -lt 0 -or -not $seenCells.Add($cellId)) { continue }

            # Primary spell: choice nodes use data-choice-href0, others use href
            $hrefToUse = if ($choice0) { $choice0 } else { $href }
            $spellId   = Get-SpellIdFromHref $hrefToUse

            # For choice nodes: replace aria-label (choice1 name) with choice0 slug name
            $ariaLabel = $nodeName  # save original; for choice nodes this IS the choice1 name
            if ($choice0) {
                $slugName = Get-NameFromHref $choice0
                if ($slugName -ne '') { $nodeName = $slugName }
            }
            $altSpellId = if ($choice1) { Get-SpellIdFromHref $choice1 } else { 0 }

            $maxRank = if ($maxRankMap.ContainsKey($cellId)) { $maxRankMap[$cellId] } else { 1 }
            $isGate  = 0   # no explicit gate marker in Wowhead HTML
            $rowPos  = if ($row -ge 0) { $row } else { [int]($cellId / 19) }
            $colPos  = if ($col -ge 0) { $col } else { $cellId % 19 }

            $cn          = Escape-Sql $className
            $sn          = Escape-Sql $specName
            $hn          = Escape-Sql $hName
            $nn          = Escape-Sql $nodeName
            $iconName    = if ($iconMap.ContainsKey($cellId))    { $iconMap[$cellId] }    else { '' }
            $altIconName = if ($altIconMap.ContainsKey($cellId)) { $altIconMap[$cellId] } else { '' }
            $iconn       = Escape-Sql $iconName
            $altIconn    = Escape-Sql $altIconName

            $nodesRows.Add(
                "('$cn', '$sn', '$hn', '$treeType', " +
                "$cellId, $rowPos, $colPos, $maxRank, $isGate, $spellId, '$nn', '$iconn', $altSpellId, '$altIconn')"
            )

            # Queue icons for later download
            $iconDestDir = Join-Path $IconBaseDir $className.ToLower()
            if ($iconName -ne '') {
                $destPath = Join-Path $iconDestDir $iconName
                if (-not $pendingIcons.ContainsKey($destPath)) {
                    $pendingIcons[$destPath] = "https://wow.zamimg.com/images/wow/icons/large/$iconName"
                }
            }
            if ($altIconName -ne '') {
                $altDestPath = Join-Path $iconDestDir $altIconName
                if (-not $pendingIcons.ContainsKey($altDestPath)) {
                    $pendingIcons[$altDestPath] = "https://wow.zamimg.com/images/wow/icons/large/$altIconName"
                }
            }

            # talent_spells: primary spell
            if ($spellId -gt 0 -and $seenSpellIds.Add($spellId)) {
                $spellsRows.Add(
                    "($spellId, '$nn', '$cn', '$sn', '$hn', '$treeType', 'nyi', NULL)"
                )
            }

            # talent_spells: secondary choice spell (if present)
            if ($altSpellId -gt 0 -and $seenSpellIds.Add($altSpellId)) {
                $altName = Get-NameFromHref $choice1
                if ($altName -eq '') { $altName = $ariaLabel }  # aria-label is choice1's name
                $an = Escape-Sql $altName
                $spellsRows.Add(
                    "($altSpellId, '$an', '$cn', '$sn', '$hn', '$treeType', 'nyi', NULL)"
                )
            }
        }

        # ---- Connections ----------------------------------------------------
        foreach ($conn in $connectionDivRx.Matches($section)) {
            $tag      = $conn.Value
            $fromCell = Get-IntAttr $tag 'data-from-cell'
            $toCell   = Get-IntAttr $tag 'data-to-cell'
            if ($fromCell -lt 0 -or $toCell -lt 0) { continue }

            $cn = Escape-Sql $className
            $sn = Escape-Sql $specName
            $hn = Escape-Sql $hName
            $connectionsRows.Add(
                "('$cn', '$sn', '$hn', '$treeType', $fromCell, $toCell)"
            )
        }
    }

    return [PSCustomObject]@{
        ClassName   = $className
        SpecName    = $specName
        HeroName    = $heroName
        Nodes       = $nodesRows
        Connections = $connectionsRows
        Spells      = $spellsRows
        Icons       = $pendingIcons
    }
}

# ---------------------------------------------------------------------------
# SQL assembly  (one file per raw input)
# ---------------------------------------------------------------------------

function Build-Sql(
    [string]$ClassName,
    [string]$SpecName,
    [string]$HeroName,
    [System.Collections.Generic.List[string]]$NodesRows,
    [System.Collections.Generic.List[string]]$ConnectionsRows,
    [System.Collections.Generic.List[string]]$SpellsRows,
    [bool]$IncludeUse = $false
) {
    $cn = Escape-Sql $ClassName
    $sn = Escape-Sql $SpecName
    $hn = Escape-Sql $HeroName

    $parts = [System.Collections.Generic.List[string]]::new()

    if ($IncludeUse) {
        $parts.Add("USE ``spellforge_tracker``;")
        $parts.Add("")
    }

    # ---- DELETE existing rows for this class/spec/hero combo ---------------
    # Class and spec tree nodes carry hero_name=''; hero tree nodes carry hero_name=HeroName.
    $parts.Add(
        "DELETE FROM ``talent_tree_connections`` WHERE ``class_name``='$cn' AND ``spec_name``='$sn' AND (``hero_name``='' OR ``hero_name``='$hn');"
    )
    $parts.Add(
        "DELETE FROM ``talent_tree_nodes`` WHERE ``class_name``='$cn' AND ``spec_name``='$sn' AND (``hero_name``='' OR ``hero_name``='$hn');"
    )
    $parts.Add("")

    # ---- talent_tree_nodes -------------------------------------------------
    if ($NodesRows.Count -gt 0) {
        $parts.Add(
            "INSERT INTO ``talent_tree_nodes``" + [Environment]::NewLine +
            "  (``class_name``, ``spec_name``, ``hero_name``, ``tree_type``," + [Environment]::NewLine +
            "   ``cell_id``, ``row_pos``, ``col_pos``, ``max_rank``, ``is_gate``," + [Environment]::NewLine +
            "   ``spell_id``, ``node_name``, ``icon_name``, ``alt_spell_id``, ``alt_icon_name``)" + [Environment]::NewLine +
            "VALUES" + [Environment]::NewLine +
            (($NodesRows | ForEach-Object { "  $_" }) -join ("," + [Environment]::NewLine)) +
            ";"
        )
        $parts.Add("")
    }

    # ---- talent_tree_connections -------------------------------------------
    if ($ConnectionsRows.Count -gt 0) {
        $parts.Add(
            "INSERT INTO ``talent_tree_connections``" + [Environment]::NewLine +
            "  (``class_name``, ``spec_name``, ``hero_name``, ``tree_type``," + [Environment]::NewLine +
            "   ``from_cell``, ``to_cell``)" + [Environment]::NewLine +
            "VALUES" + [Environment]::NewLine +
            (($ConnectionsRows | ForEach-Object { "  $_" }) -join ("," + [Environment]::NewLine)) +
            ";"
        )
        $parts.Add("")
    }

    # ---- talent_spells  (ON DUPLICATE KEY UPDATE preserves status/notes) --
    if ($SpellsRows.Count -gt 0) {
        $parts.Add(
            "INSERT INTO ``talent_spells``" + [Environment]::NewLine +
            "  (``spell_id``, ``spell_name``, ``class_name``, ``spec_name``, ``hero_name``," + [Environment]::NewLine +
            "   ``tree_type``, ``status``, ``notes``)" + [Environment]::NewLine +
            "VALUES" + [Environment]::NewLine +
            (($SpellsRows | ForEach-Object { "  $_" }) -join ("," + [Environment]::NewLine)) + [Environment]::NewLine +
            "AS _new" + [Environment]::NewLine +
            "ON DUPLICATE KEY UPDATE" + [Environment]::NewLine +
            "  ``spell_name`` = _new.``spell_name``," + [Environment]::NewLine +
            "  ``status`` = IF(``talent_spells``.``status`` = 'unknown', _new.``status``, ``talent_spells``.``status``);"
        )
        $parts.Add("")
    }

    return $parts -join [Environment]::NewLine
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if (-not $Files -or $Files.Count -eq 0) {
    $Files = Get-ChildItem -Path $ScriptDir -Filter 'raw_*.txt' |
             Sort-Object Name |
             Select-Object -ExpandProperty FullName
}

if ($Files.Count -eq 0) {
    Write-Error "No raw_*.txt files found in $ScriptDir"
    exit 1
}

$allIconPending = @{}  # destPath -> url  (deduplicated across all files)
$allSqlBlocks   = [System.Collections.Generic.List[string]]::new()

foreach ($file in $Files) {
    $leafName = Split-Path -Leaf $file
    Write-Host "Parsing $leafName ..."

    $result = Parse-File $file
    if ($null -eq $result) { continue }

    $sql = Build-Sql `
        $result.ClassName $result.SpecName $result.HeroName `
        $result.Nodes $result.Connections $result.Spells

    $blockComment = "-- [$leafName]"
    $allSqlBlocks.Add($blockComment + [Environment]::NewLine + $sql)

    Write-Host ("  -> {0} nodes, {1} connections, {2} spells, {3} icons queued" -f `
        $result.Nodes.Count, $result.Connections.Count, $result.Spells.Count, $result.Icons.Count)

    foreach ($dest in $result.Icons.Keys) {
        if (-not $allIconPending.ContainsKey($dest)) {
            $allIconPending[$dest] = $result.Icons[$dest]
        }
    }
}

# Write single combined SQL file
if ($allSqlBlocks.Count -gt 0) {
    $masterHeader = "-- Auto-generated by parse_raw.ps1 -- do not edit by hand." +
                    [Environment]::NewLine +
                    "-- Contains all classes/specs/heroes processed in this run." +
                    [Environment]::NewLine + [Environment]::NewLine +
                    "USE ``spellforge_tracker``;" + [Environment]::NewLine + [Environment]::NewLine
    $combined = $masterHeader + ($allSqlBlocks -join ([Environment]::NewLine + [Environment]::NewLine))
    $outPath  = Join-Path $ScriptDir 'all_talents.sql'
    [System.IO.File]::WriteAllText($outPath, $combined + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
    Write-Host ""
    Write-Host "-> Combined SQL written: $outPath"
}

Write-Host ""
Write-Host ("Total unique icons queued: {0}" -f $allIconPending.Count)

# Download spell icons (skips files that already exist)
if ($allIconPending.Count -gt 0) {
    Write-Host "Downloading spell icons to $IconBaseDir ..."
    $iconArray = @($allIconPending.GetEnumerator() | ForEach-Object {
        [PSCustomObject]@{ Dest = $_.Key; Url = $_.Value }
    })
    Download-Icons $iconArray
}

# Download class icons (always runs; skips files that already exist)
Write-Host ""
Write-Host "Downloading class icons to $ClassIconBaseDir ..."
Download-ClassIcons
