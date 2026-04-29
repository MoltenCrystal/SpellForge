-- ============================================================
-- spellforge_sniffs  —  WPP sniff import database
-- Run once (or re-run safely): all statements are idempotent.
-- Separate from spellforge_tracker; never share tables.
-- ============================================================

CREATE DATABASE IF NOT EXISTS `spellforge_sniffs`
    DEFAULT CHARACTER SET utf8mb4
    DEFAULT COLLATE utf8mb4_unicode_ci;

USE `spellforge_sniffs`;

-- --------------------------------------------------------
-- One row per imported _parsed.txt file
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_sessions` (
  `id`            INT UNSIGNED  NOT NULL AUTO_INCREMENT,
  `file_name`     VARCHAR(512)  NOT NULL COMMENT 'Original absolute path of the parsed txt file',
  `build_version` VARCHAR(32)   NOT NULL DEFAULT '' COMMENT 'e.g. 12.0.1.66838, parsed from filename',
  `imported_at`   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `packet_count`  INT UNSIGNED  NOT NULL DEFAULT 0 COMMENT 'Total packets recognised in this file',
  `notes`         TEXT,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_file` (`file_name`(512))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- One row per SMSG_SPELL_GO (completed cast)
-- SMSG_SPELL_START is intentionally excluded — no hit data
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_spell_casts` (
  `id`                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`            INT UNSIGNED    NOT NULL,
  `packet_number`         INT UNSIGNED    NOT NULL,
  `timestamp_ms`          BIGINT UNSIGNED NOT NULL COMMENT 'Milliseconds from first packet in session',
  `spell_id`              INT UNSIGNED    NOT NULL,
  `caster_type`           VARCHAR(32)     NOT NULL DEFAULT '' COMMENT 'Player, Creature, Vehicle, etc.',
  `caster_guid`           VARCHAR(34)     NOT NULL DEFAULT '' COMMENT 'Hex GUID string',
  `target_type`           VARCHAR(32)     NOT NULL DEFAULT '',
  `target_guid`           VARCHAR(34)     NOT NULL DEFAULT '',
  `cast_time_ms`          INT UNSIGNED    NOT NULL DEFAULT 0 COMMENT '0 = instant cast',
  `cast_flags`            INT UNSIGNED    NOT NULL DEFAULT 0,
  `is_triggered`          TINYINT(1)      NOT NULL DEFAULT 0 COMMENT '1 when CastFlags & 0x80 != 0',
  `hit_count`             SMALLINT        NOT NULL DEFAULT 0,
  `miss_count`            SMALLINT        NOT NULL DEFAULT 0,
  `triggered_by_spell_id` INT UNSIGNED             DEFAULT NULL COMMENT 'Inferred via 100ms time-window proc chain',
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`),
  KEY `idx_ts`         (`session_id`, `timestamp_ms`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- One row per aura slot entry in SMSG_AURA_UPDATE
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_aura_events` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`    INT UNSIGNED    NOT NULL,
  `packet_number` INT UNSIGNED    NOT NULL,
  `timestamp_ms`  BIGINT UNSIGNED NOT NULL,
  `spell_id`      INT UNSIGNED    NOT NULL,
  `unit_type`     VARCHAR(32)     NOT NULL DEFAULT '' COMMENT 'Unit that has the aura',
  `unit_guid`     VARCHAR(34)     NOT NULL DEFAULT '',
  `caster_type`   VARCHAR(32)     NOT NULL DEFAULT '' COMMENT 'Who cast the aura',
  `caster_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `event_type`    ENUM('APPLIED','REMOVED','REFRESHED','STACK_CHANGED') NOT NULL DEFAULT 'APPLIED',
  `slot`          SMALLINT        NOT NULL DEFAULT 0,
  `stack_count`   TINYINT UNSIGNED NOT NULL DEFAULT 1,
  `duration_ms`   INT             NOT NULL DEFAULT 0,
  `remaining_ms`  INT             NOT NULL DEFAULT 0,
  `aura_flags`    SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`),
  KEY `idx_unit`       (`unit_guid`(34), `spell_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- SMSG_SPELL_NON_MELEE_DAMAGE_LOG
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_damage_events` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`    INT UNSIGNED    NOT NULL,
  `packet_number` INT UNSIGNED    NOT NULL,
  `timestamp_ms`  BIGINT UNSIGNED NOT NULL,
  `spell_id`      INT UNSIGNED    NOT NULL,
  `caster_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `caster_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `target_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `target_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `school_mask`   TINYINT UNSIGNED NOT NULL DEFAULT 0,
  `damage`        INT UNSIGNED    NOT NULL DEFAULT 0,
  `overkill`      INT             NOT NULL DEFAULT 0,
  `absorbed`      INT             NOT NULL DEFAULT 0,
  `resisted`      INT             NOT NULL DEFAULT 0,
  `blocked`       INT             NOT NULL DEFAULT 0,
  `is_periodic`   TINYINT(1)      NOT NULL DEFAULT 0,
  `is_critical`   TINYINT(1)      NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- SMSG_SPELL_HEAL_LOG
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_heal_events` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`    INT UNSIGNED    NOT NULL,
  `packet_number` INT UNSIGNED    NOT NULL,
  `timestamp_ms`  BIGINT UNSIGNED NOT NULL,
  `spell_id`      INT UNSIGNED    NOT NULL,
  `caster_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `caster_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `target_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `target_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `heal`          INT UNSIGNED    NOT NULL DEFAULT 0,
  `overheal`      INT             NOT NULL DEFAULT 0,
  `absorbed`      INT             NOT NULL DEFAULT 0,
  `is_periodic`   TINYINT(1)      NOT NULL DEFAULT 0,
  `is_critical`   TINYINT(1)      NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- SMSG_SPELL_ENERGIZE_LOG
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_energize_events` (
  `id`             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`     INT UNSIGNED    NOT NULL,
  `packet_number`  INT UNSIGNED    NOT NULL,
  `timestamp_ms`   BIGINT UNSIGNED NOT NULL,
  `spell_id`       INT UNSIGNED    NOT NULL,
  `caster_type`    VARCHAR(32)     NOT NULL DEFAULT '',
  `caster_guid`    VARCHAR(34)     NOT NULL DEFAULT '',
  `target_type`    VARCHAR(32)     NOT NULL DEFAULT '',
  `target_guid`    VARCHAR(34)     NOT NULL DEFAULT '',
  `power_type`     TINYINT         NOT NULL DEFAULT 0 COMMENT '0=Mana 1=Rage 2=Focus 3=Energy 6=RunicPower etc.',
  `amount`         INT             NOT NULL DEFAULT 0,
  `over_energize`  INT             NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- SMSG_SPELL_PERIODIC_AURA_LOG
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_periodic_events` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `session_id`    INT UNSIGNED    NOT NULL,
  `packet_number` INT UNSIGNED    NOT NULL,
  `timestamp_ms`  BIGINT UNSIGNED NOT NULL,
  `spell_id`      INT UNSIGNED    NOT NULL,
  `caster_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `caster_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `target_type`   VARCHAR(32)     NOT NULL DEFAULT '',
  `target_guid`   VARCHAR(34)     NOT NULL DEFAULT '',
  `aura_type`     TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'SPELL_AURA_PERIODIC_* numeric value from Effect field',
  `school_mask`   TINYINT UNSIGNED NOT NULL DEFAULT 0,
  `amount`        INT             NOT NULL DEFAULT 0,
  `over_amount`   INT             NOT NULL DEFAULT 0,
  `absorbed`      INT             NOT NULL DEFAULT 0,
  `is_critical`   TINYINT(1)      NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_spell_id`   (`spell_id`),
  KEY `idx_session_id` (`session_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------
-- Migrations: fix column types for existing installations.
-- ALTER TABLE MODIFY COLUMN is safe to re-run on already-correct schemas.
-- --------------------------------------------------------
ALTER TABLE `sniff_damage_events` MODIFY COLUMN `overkill`  INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_heal_events`   MODIFY COLUMN `overheal`  INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_damage_events` MODIFY COLUMN `absorbed`  INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_damage_events` MODIFY COLUMN `resisted`  INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_damage_events` MODIFY COLUMN `blocked`   INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_heal_events`   MODIFY COLUMN `absorbed`  INT NOT NULL DEFAULT 0;
ALTER TABLE `sniff_periodic_events` MODIFY COLUMN `absorbed` INT NOT NULL DEFAULT 0;

-- --------------------------------------------------------
-- Spell prepare observations — one row per unique talent spell observed going
-- through the SMSG_SPELL_PREPARE / SMSG_SPELL_START pair.
--
-- Fields that matter from the sniff:
--   client_spell_id   — SpellID from SMSG_SPELL_START (the talent the player cast,
--                       e.g. 431044 Frostfire Bolt).  Authoritative; comes from the
--                       SpellID field, NOT from any GUID Entry.
--   server_cast_spell — ServerCastID GUID Entry decoded by WPP (e.g. 116 Frostbolt).
--                       Informational only — "it's just a guid, you could skip the
--                       spell ID in it and nothing would change" — but it hints at
--                       the underlying spell the server runs internally.
--   visual_id         — SpellXSpellVisualID from SMSG_SPELL_START: the visual the
--                       server used for this cast (what the client displays).
--   cast_flags        — CastFlags from SMSG_SPELL_START.
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_spell_substitutions` (
  `client_spell_id`   INT UNSIGNED NOT NULL COMMENT 'SpellID from SMSG_SPELL_START (talent spell, e.g. 431044)',
  `server_cast_spell` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'ServerCastID GUID Entry hint (e.g. 116) — informational, not authoritative',
  `visual_id`         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'SpellXSpellVisualID from SMSG_SPELL_START',
  `cast_flags`        INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'CastFlags from SMSG_SPELL_START',
  `observe_count`     INT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Number of SMSG_SPELL_PREPARE/SPELL_START pair observations',
  PRIMARY KEY (`client_spell_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── Migrations for existing installs ────────────────────────────────────────
-- EnsureSchema() swallows errors on non-critical statements, so it is safe for
-- these to fail when the schema is already up to date.  Each ALTER is a
-- separate statement so one failure never blocks the rest.

-- From original schema (client_spell_id + server_spell_id composite PK):
ALTER TABLE `sniff_spell_substitutions` DROP KEY `idx_server_spell_id`;
ALTER TABLE `sniff_spell_substitutions` DROP COLUMN `server_spell_id`;

-- From intermediate schema (spell_id single PK):
ALTER TABLE `sniff_spell_substitutions`
  CHANGE COLUMN `spell_id` `client_spell_id` INT UNSIGNED NOT NULL
    COMMENT 'SpellID from SMSG_SPELL_START (talent spell, e.g. 431044)';

-- Add new columns if missing.  Fails silently (via EnsureSchema) when already present:
ALTER TABLE `sniff_spell_substitutions`
  ADD COLUMN `server_cast_spell` INT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'ServerCastID GUID Entry hint (e.g. 116) — informational, not authoritative'
    AFTER `client_spell_id`;
ALTER TABLE `sniff_spell_substitutions`
  ADD COLUMN `visual_id` INT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'SpellXSpellVisualID from SMSG_SPELL_START'
    AFTER `server_cast_spell`;
ALTER TABLE `sniff_spell_substitutions`
  ADD COLUMN `cast_flags` INT UNSIGNED NOT NULL DEFAULT 0
    COMMENT 'CastFlags from SMSG_SPELL_START'
    AFTER `visual_id`;

-- Recomputed by SniffImporter after each import batch.
-- Fast primary-key lookup; avoids aggregation at query time.
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS `sniff_spell_summary` (
  `spell_id`           INT UNSIGNED NOT NULL,
  `total_casts`        INT UNSIGNED NOT NULL DEFAULT 0,
  `instant_cast_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `triggered_count`    INT UNSIGNED NOT NULL DEFAULT 0,
  `unique_sessions`    INT UNSIGNED NOT NULL DEFAULT 0,
  `avg_damage`         DOUBLE       NOT NULL DEFAULT 0,
  `min_damage`         INT UNSIGNED NOT NULL DEFAULT 0,
  `max_damage`         INT UNSIGNED NOT NULL DEFAULT 0,
  `avg_heal`           DOUBLE       NOT NULL DEFAULT 0,
  `avg_energize`       DOUBLE       NOT NULL DEFAULT 0,
  `aura_applied_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `aura_removed_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `last_seen_build`    VARCHAR(32)  NOT NULL DEFAULT '',
  `last_updated`       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`spell_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
