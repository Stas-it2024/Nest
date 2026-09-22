
CREATE DATABASE IF NOT EXISTS `hotel` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
USE `hotel`;

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
DROP TABLE IF EXISTS `autovermietungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `autovermietungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `reservierung_id` int(11) DEFAULT NULL,
  `fahrer_name` varchar(100) NOT NULL,
  `telefon` varchar(30) NOT NULL,
  `fahrzeug_kategorie` varchar(50) NOT NULL,
  `abhol_datum` date NOT NULL,
  `rueckgabe_datum` date NOT NULL,
  `tagespreis` decimal(10,2) NOT NULL,
  `gesamtpreis` decimal(10,2) NOT NULL,
  `status` enum('Angefragt','Bestaetigt','Abgeschlossen','Storniert') NOT NULL DEFAULT 'Angefragt',
  `erstellt_am` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `fk_autovermietung_reservierung` (`reservierung_id`),
  KEY `idx_autovermietung_daten` (`abhol_datum`,`rueckgabe_datum`),
  KEY `idx_autovermietung_status` (`status`),
  CONSTRAINT `fk_autovermietung_reservierung` FOREIGN KEY (`reservierung_id`) REFERENCES `reservierungen` (`id`) ON DELETE SET NULL,
  CONSTRAINT `chk_autovermietung_daten` CHECK (`rueckgabe_datum` > `abhol_datum`),
  CONSTRAINT `chk_autovermietung_tagespreis` CHECK (`tagespreis` >= 0),
  CONSTRAINT `chk_autovermietung_gesamtpreis` CHECK (`gesamtpreis` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `autovermietungen` WRITE;
/*!40000 ALTER TABLE `autovermietungen` DISABLE KEYS */;
/*!40000 ALTER TABLE `autovermietungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `benutzer`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `benutzer` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `benutzername` varchar(50) NOT NULL,
  `passwort` varchar(255) NOT NULL,
  `vollstaendiger_name` varchar(100) NOT NULL,
  `rolle` enum('Admin','Rezeptionist') NOT NULL DEFAULT 'Rezeptionist',
  `status` enum('Aktiv','Inaktiv') NOT NULL DEFAULT 'Aktiv',
  `hinzugefuegt_am` datetime DEFAULT current_timestamp(),
  `geloescht_am` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `benutzername` (`benutzername`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `benutzer` WRITE;
/*!40000 ALTER TABLE `benutzer` DISABLE KEYS */;
INSERT INTO `benutzer` VALUES (1,'admin','admin123','Systemadministrator','Admin','Aktiv','2026-09-15 09:43:40',NULL),(2,'receptionist','rec123','Empfangspersonal','Rezeptionist','Aktiv','2026-09-15 09:43:40',NULL),(3,'admin1','123','Systemadministrator','Admin','Aktiv','2026-09-17 13:42:03','2026-09-17 13:42:22');
/*!40000 ALTER TABLE `benutzer` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `dienstleistungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `dienstleistungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `dienstleistungs_name` varchar(100) NOT NULL,
  `kategorie` enum('Speisen und Getraenke','Waesche','Wellness','Transport','Sonstiges') NOT NULL DEFAULT 'Sonstiges',
  `preis` decimal(10,2) NOT NULL,
  `status` enum('Verfuegbar','Nicht_verfuegbar') NOT NULL DEFAULT 'Verfuegbar',
  `hinzugefuegt_am` datetime DEFAULT current_timestamp(),
  `geloescht_am` datetime DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `dienstleistungen` WRITE;
/*!40000 ALTER TABLE `dienstleistungen` DISABLE KEYS */;
INSERT INTO `dienstleistungen` VALUES (1,'Fruehstuecksbuffet','Speisen und Getraenke',18.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(2,'Zimmerservice-Mahlzeit','Speisen und Getraenke',25.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(3,'Waesche (pro kg)','Waesche',8.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(4,'Chemische Reinigung','Waesche',15.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(5,'Wellness - Ganzkoerperbehandlung','Wellness',80.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(6,'Wellness - Massage','Wellness',60.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(7,'Flughafentransfer','Transport',40.00,'Verfuegbar','2026-09-15 09:43:40',NULL),(8,'Stadtrundfahrt','Transport',55.00,'Verfuegbar','2026-09-15 09:43:40',NULL);
/*!40000 ALTER TABLE `dienstleistungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `dienstleistungsbestellungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `dienstleistungsbestellungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `reservierung_id` int(11) NOT NULL,
  `dienstleistung_id` int(11) NOT NULL,
  `anzahl` int(11) NOT NULL DEFAULT 1,
  `einzelpreis` decimal(10,2) NOT NULL,
  `gesamtpreis` decimal(10,2) NOT NULL,
  `bestellt_am` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `reservierung_id` (`reservierung_id`),
  KEY `dienstleistung_id` (`dienstleistung_id`),
  CONSTRAINT `dienstleistungsbestellungen_ibfk_1` FOREIGN KEY (`reservierung_id`) REFERENCES `reservierungen` (`id`),
  CONSTRAINT `dienstleistungsbestellungen_ibfk_2` FOREIGN KEY (`dienstleistung_id`) REFERENCES `dienstleistungen` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `dienstleistungsbestellungen` WRITE;
/*!40000 ALTER TABLE `dienstleistungsbestellungen` DISABLE KEYS */;
/*!40000 ALTER TABLE `dienstleistungsbestellungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `gaeste`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `gaeste` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `vorname` varchar(50) NOT NULL,
  `nachname` varchar(50) NOT NULL,
  `email` varchar(100) DEFAULT NULL,
  `telefon` varchar(20) DEFAULT NULL,
  `ausweis_typ` enum('Reisepass','Personalausweis','Fuehrerschein','Sonstiges') DEFAULT 'Reisepass',
  `ausweis_nummer` varchar(50) DEFAULT NULL,
  `nationalitaet` varchar(50) DEFAULT NULL,
  `hinzugefuegt_am` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `gaeste` WRITE;
/*!40000 ALTER TABLE `gaeste` DISABLE KEYS */;
INSERT INTO `gaeste` VALUES (1,'Stas','Kharchenko','asdf','1234',NULL,NULL,'Ukraine','2026-09-17 14:34:36');
/*!40000 ALTER TABLE `gaeste` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `reservierungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `reservierungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `reservierungs_code` varchar(20) NOT NULL,
  `gast_id` int(11) NOT NULL,
  `zimmer_id` int(11) NOT NULL,
  `anreise_datum` date NOT NULL,
  `abreise_datum` date NOT NULL,
  `naechte` int(11) NOT NULL DEFAULT 1,
  `erwachsene` int(11) NOT NULL DEFAULT 1,
  `kinder` int(11) NOT NULL DEFAULT 0,
  `gesamtbetrag` decimal(10,2) NOT NULL DEFAULT 0.00,
  `gezahlter_betrag` decimal(10,2) NOT NULL DEFAULT 0.00,
  `zahlungsart` enum('Bar','Kreditkarte','Debitkarte','Ueberweisung','Online') DEFAULT 'Bar',
  `status` enum('Reserviert','Eingecheckt','Ausgecheckt','Storniert') NOT NULL DEFAULT 'Reserviert',
  `sonderwuensche` text DEFAULT NULL,
  `rezeptionist_id` int(11) DEFAULT NULL,
  `erstellt_am` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `reservierungs_code` (`reservierungs_code`),
  KEY `gast_id` (`gast_id`),
  KEY `zimmer_id` (`zimmer_id`),
  CONSTRAINT `reservierungen_ibfk_1` FOREIGN KEY (`gast_id`) REFERENCES `gaeste` (`id`),
  CONSTRAINT `reservierungen_ibfk_2` FOREIGN KEY (`zimmer_id`) REFERENCES `zimmer` (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `reservierungen` WRITE;
/*!40000 ALTER TABLE `reservierungen` DISABLE KEYS */;
INSERT INTO `reservierungen` VALUES (1,'72F971CC',1,8,'2026-09-18','2026-09-20',2,2,0,899.98,0.00,'Bar','Reserviert','',NULL,'2026-09-17 14:34:36');
/*!40000 ALTER TABLE `reservierungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `restaurant_reservierungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `restaurant_reservierungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `reservierung_id` int(11) DEFAULT NULL,
  `gast_name` varchar(100) NOT NULL,
  `reservierungs_datum` date NOT NULL,
  `reservierungs_zeit` time NOT NULL,
  `gast_anzahl` int(11) NOT NULL,
  `anlass` varchar(50) NOT NULL,
  `sonderwuensche` text DEFAULT NULL,
  `status` enum('Angefragt','Bestaetigt','Abgeschlossen','Storniert') NOT NULL DEFAULT 'Angefragt',
  `erstellt_am` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `fk_restaurant_reservation` (`reservierung_id`),
  KEY `idx_restaurant_datetime` (`reservierungs_datum`,`reservierungs_zeit`),
  KEY `idx_restaurant_status` (`status`),
  CONSTRAINT `fk_restaurant_reservation` FOREIGN KEY (`reservierung_id`) REFERENCES `reservierungen` (`id`) ON DELETE SET NULL,
  CONSTRAINT `chk_restaurant_gastanzahl` CHECK (`gast_anzahl` between 1 and 8)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `restaurant_reservierungen` WRITE;
/*!40000 ALTER TABLE `restaurant_reservierungen` DISABLE KEYS */;
/*!40000 ALTER TABLE `restaurant_reservierungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `taxibestellungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `taxibestellungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `reservierung_id` int(11) DEFAULT NULL,
  `gast_name` varchar(100) NOT NULL,
  `gast_telefon` varchar(30) DEFAULT NULL,
  `abholort` varchar(255) NOT NULL,
  `abhol_datum` date NOT NULL,
  `abhol_zeit` time NOT NULL,
  `fahrzeug_typ` varchar(50) NOT NULL,
  `status` enum('Angefragt','Bestaetigt','Abgeschlossen','Storniert') NOT NULL DEFAULT 'Angefragt',
  `bestellt_am` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `fk_taxibestellung_reservierung` (`reservierung_id`),
  KEY `idx_taxi_abholung` (`abhol_datum`,`abhol_zeit`),
  KEY `idx_taxi_status` (`status`),
  CONSTRAINT `fk_taxibestellung_reservierung` FOREIGN KEY (`reservierung_id`) REFERENCES `reservierungen` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `taxibestellungen` WRITE;
/*!40000 ALTER TABLE `taxibestellungen` DISABLE KEYS */;
/*!40000 ALTER TABLE `taxibestellungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `ticketbestellungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ticketbestellungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `veranstaltung_id` int(11) NOT NULL,
  `gast_name` varchar(100) NOT NULL,
  `gast_email` varchar(100) DEFAULT NULL,
  `gast_telefon` varchar(30) DEFAULT NULL,
  `anzahl` int(11) NOT NULL DEFAULT 1,
  `einzelpreis` decimal(10,2) NOT NULL,
  `gesamtpreis` decimal(10,2) NOT NULL,
  `ticket_nummer` varchar(30) NOT NULL,
  `reservierung_id` int(11) DEFAULT NULL,
  `bestellt_am` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `ticket_nummer` (`ticket_nummer`),
  KEY `event_id` (`veranstaltung_id`),
  CONSTRAINT `ticketbestellungen_ibfk_1` FOREIGN KEY (`veranstaltung_id`) REFERENCES `veranstaltungen` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `ticketbestellungen` WRITE;
/*!40000 ALTER TABLE `ticketbestellungen` DISABLE KEYS */;
/*!40000 ALTER TABLE `ticketbestellungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `veranstaltungen`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `veranstaltungen` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `titel` varchar(200) NOT NULL,
  `beschreibung` text DEFAULT NULL,
  `veranstaltungs_datum` date NOT NULL,
  `veranstaltungs_zeit` time DEFAULT '19:00:00',
  `ort` varchar(200) DEFAULT 'Hotel Lobby',
  `ticket_preis` decimal(10,2) NOT NULL DEFAULT 0.00,
  `maximale_tickets` int(11) DEFAULT 100,
  `verkaufte_tickets` int(11) DEFAULT 0,
  `bild_url` varchar(500) DEFAULT NULL,
  `status` enum('Aktiv','Inaktiv','Ausgebucht') NOT NULL DEFAULT 'Aktiv',
  `erstellt_von` int(11) DEFAULT NULL,
  `erstellt_am` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `veranstaltungen` WRITE;
/*!40000 ALTER TABLE `veranstaltungen` DISABLE KEYS */;
INSERT INTO `veranstaltungen` VALUES (1,'Willkommens-Abend','Taeglich: Begruessung der Gaeste mit Cocktails und Live-Musik im Panorama-Saal.','2026-09-15','19:00:00','Panorama-Saal',0.00,200,0,NULL,'Aktiv',NULL,'2026-09-15 09:43:41'),(2,'Wein- und Genussabend','Exklusives 5-Gang-Menue mit erlesenen Weinen aus aller Welt.','2026-09-16','19:30:00','Restaurant Grand',85.00,40,0,NULL,'Aktiv',NULL,'2026-09-15 09:43:41'),(3,'Yoga am Morgen','Entspannende Yoga-Session am Pool fuer alle Gaeste.','2026-09-15','07:30:00','Poolterrasse',15.00,20,0,NULL,'Aktiv',NULL,'2026-09-15 09:43:41'),(4,'Live-Jazzabend','Jazztrio spielt klassischen Jazz und Soul. Getraenke inklusive.','2026-09-17','20:00:00','Hotelbar',25.00,60,0,NULL,'Aktiv',NULL,'2026-09-15 09:43:41');
/*!40000 ALTER TABLE `veranstaltungen` ENABLE KEYS */;
UNLOCK TABLES;
DROP TABLE IF EXISTS `zimmer`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `zimmer` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zimmer_nummer` varchar(10) NOT NULL,
  `zimmer_typ` enum('Standard','Deluxe','Suite','Executive') NOT NULL DEFAULT 'Standard',
  `kapazitaet` int(11) NOT NULL DEFAULT 1,
  `preis_pro_nacht` decimal(10,2) NOT NULL,
  `status` enum('Verfuegbar','Belegt','Wartung','Reserviert') NOT NULL DEFAULT 'Verfuegbar',
  `beschreibung` text DEFAULT NULL,
  `bild_pfad` varchar(500) DEFAULT NULL,
  `hinzugefuegt_am` datetime DEFAULT current_timestamp(),
  `geloescht_am` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `zimmer_nummer` (`zimmer_nummer`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `zimmer` WRITE;
/*!40000 ALTER TABLE `zimmer` DISABLE KEYS */;
INSERT INTO `zimmer` VALUES (1,'101','Standard',1,59.99,'Verfuegbar','Gemuetliches Standard-Einzelzimmer mit Stadtblick',NULL,'2026-09-15 09:43:40',NULL),(2,'102','Standard',2,79.99,'Verfuegbar','Standard-Zweibettzimmer im Erdgeschoss',NULL,'2026-09-15 09:43:40',NULL),(3,'201','Deluxe',2,119.99,'Verfuegbar','Deluxe-Doppelzimmer mit Balkon',NULL,'2026-09-15 09:43:40',NULL),(4,'202','Deluxe',2,129.99,'Verfuegbar','Deluxe-Kingsize-Zimmer mit Meerblick',NULL,'2026-09-15 09:43:40',NULL),(5,'301','Suite',4,249.99,'Verfuegbar','Junior-Suite mit Wohnbereich und Kochnische',NULL,'2026-09-15 09:43:40',NULL),(6,'302','Suite',4,299.99,'Verfuegbar','Familien-Suite mit zwei Schlafzimmern',NULL,'2026-09-15 09:43:40',NULL),(7,'401','Executive',2,399.99,'Verfuegbar','Exklusive Etage mit Panoramablick und Butlerservice',NULL,'2026-09-15 09:43:40',NULL),(8,'402','Executive',2,449.99,'Verfuegbar','Praesidentensuite mit Zugang zur gesamten Etage',NULL,'2026-09-15 09:43:40',NULL);
/*!40000 ALTER TABLE `zimmer` ENABLE KEYS */;
UNLOCK TABLES;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;
