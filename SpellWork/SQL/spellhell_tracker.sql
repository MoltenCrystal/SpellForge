-- --------------------------------------------------------
-- Host:                         127.0.0.1
-- Server version:               8.0.45 - MySQL Community Server - GPL
-- Server OS:                    Win64
-- HeidiSQL Version:             12.15.0.7171
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;


-- Dumping database structure for spellhell_tracker
DROP DATABASE IF EXISTS `spellhell_tracker`;
CREATE DATABASE IF NOT EXISTS `spellhell_tracker` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */ /*!80016 DEFAULT ENCRYPTION='N' */;
USE `spellhell_tracker`;

-- Dumping structure for table spellhell_tracker.talent_spells
DROP TABLE IF EXISTS `talent_spells`;
CREATE TABLE IF NOT EXISTS `talent_spells` (
  `spell_id` int NOT NULL,
  `spell_name` varchar(255) NOT NULL DEFAULT '',
  `class_name` varchar(50) NOT NULL DEFAULT '',
  `spec_name` varchar(50) NOT NULL DEFAULT '',
  `hero_name` varchar(50) NOT NULL DEFAULT '',
  `tree_type` varchar(10) NOT NULL DEFAULT '' COMMENT 'class | spec | hero',
  `status` varchar(50) NOT NULL DEFAULT 'nyi' COMMENT 'nyi | wip | done',
  `notes` text,
  `last_updated` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`spell_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Data exporting was unselected.

-- Dumping structure for table spellhell_tracker.talent_tree_connections
DROP TABLE IF EXISTS `talent_tree_connections`;
CREATE TABLE IF NOT EXISTS `talent_tree_connections` (
  `id` int NOT NULL AUTO_INCREMENT,
  `class_name` varchar(50) NOT NULL,
  `spec_name` varchar(50) NOT NULL,
  `hero_name` varchar(50) NOT NULL DEFAULT '',
  `tree_type` varchar(10) NOT NULL,
  `from_cell` int NOT NULL,
  `to_cell` int NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_conn` (`class_name`,`spec_name`,`hero_name`,`tree_type`,`from_cell`,`to_cell`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Data exporting was unselected.

-- Dumping structure for table spellhell_tracker.talent_tree_nodes
DROP TABLE IF EXISTS `talent_tree_nodes`;
CREATE TABLE IF NOT EXISTS `talent_tree_nodes` (
  `class_name` varchar(50) NOT NULL,
  `spec_name` varchar(50) NOT NULL,
  `hero_name` varchar(50) NOT NULL DEFAULT '',
  `tree_type` varchar(10) NOT NULL COMMENT 'class | spec | hero',
  `cell_id` int NOT NULL COMMENT 'Wowhead data-cell value',
  `row_pos` int NOT NULL,
  `col_pos` int NOT NULL,
  `max_rank` int NOT NULL DEFAULT '1',
  `is_gate` tinyint(1) NOT NULL DEFAULT '0',
  `spell_id` int NOT NULL DEFAULT '0',
  `node_name` varchar(255) NOT NULL DEFAULT '',
  `icon_name` varchar(120) NOT NULL DEFAULT '',
  `alt_spell_id` int NOT NULL DEFAULT '0',
  `alt_icon_name` varchar(120) NOT NULL DEFAULT '',
  PRIMARY KEY (`class_name`,`spec_name`,`hero_name`,`tree_type`,`cell_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Data exporting was unselected.

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
