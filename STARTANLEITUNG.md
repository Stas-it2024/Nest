# STARTANLEITUNG – Nest Hotel Management System

> **ACHTUNG – Datenverlust bei erneutem Import:** Für die Erstinstallation der Demo-Datenbank ist ausschließlich `Nest/Database/hotel_deutsch.sql` vorgesehen. Das Skript enthält `USE hotel` (im SQL mit Backticks geschrieben) und `DROP TABLE IF EXISTS`. Ein erneuter Import löscht die vorhandenen Daten in den elf enthaltenen Tabellen der Datenbank `hotel` und ersetzt sie durch Demodaten. Eine andere Auswahl in phpMyAdmin schützt `hotel` wegen der `USE`-Anweisung nicht. Nicht in eine bestehende Datenbank mit erhaltenswerten Daten importieren; vorher eine Sicherung erstellen und das weitere Vorgehen klären.

## Schnellstart

Für die erstmalige Einrichtung einer neuen Demo-Installation:

```text
1. XAMPP installieren
        ↓
2. Apache + MySQL starten
        ↓
3. http://localhost/phpmyadmin öffnen
        ↓
4. Nur bei neuer Demo-Datenbank: Nest/Database/hotel_deutsch.sql importieren
        ↓
5. Nest-Projekt in Visual Studio öffnen
        ↓
6. Datenbankverbindung prüfen
        ↓
7. F5 → Programm starten
```

---

# 1. XAMPP installieren

Für das Projekt wird eine lokale MySQL-/MariaDB-Datenbank benötigt. Der Datenbankdienst heißt im XAMPP Control Panel **MySQL**.

Dafür wird **XAMPP** verwendet.

Nach der Installation von XAMPP wird das **XAMPP Control Panel** geöffnet.

---

# 2. Apache + MySQL starten

Im XAMPP Control Panel müssen folgende Dienste gestartet werden:

* **Apache**
* **MySQL**

Für die Einrichtung über phpMyAdmin müssen beide Dienste erfolgreich gestartet sein.

**Apache** wird für die Weboberfläche phpMyAdmin benötigt. Das WPF-Programm verbindet sich über den Datenbanktreiber direkt mit MySQL/MariaDB und benötigt selbst keinen Apache-Dienst. Nach der Einrichtung genügt für den Betrieb der Anwendung der laufende Datenbankdienst.

Die Anwendung verwendet folgende Datenbankverbindung:

| Einstellung | Wert        |
| ----------- | ----------- |
| Server      | `localhost` |
| Port        | `3306`      |
| Datenbank   | `hotel`     |
| Benutzer    | `root`      |
| Passwort    | `root`      |

> **Wichtig:** Der MySQL-Dienst muss auf Port `3306` laufen.

---

# 3. phpMyAdmin öffnen

Nach dem Start von Apache und MySQL wird phpMyAdmin im Browser geöffnet:

```text
http://localhost/phpmyadmin
```

phpMyAdmin wird verwendet, um die Datenbank für das Projekt einzurichten und zu verwalten.

---

# 4. SQL-Datei importieren

Der aktuelle SQL-Einstiegspunkt für die Erstinstallation ist [Nest/Database/hotel_deutsch.sql](Nest/Database/hotel_deutsch.sql). Diese Datei enthält die deutsche Datenbankstruktur und Demodaten für den aktuellen Anwendungscode. Die ältere Datei `hotel_database.sql` im Repository-Stamm ist dafür nicht zu verwenden.

> **ACHTUNG: Kein Reparatur- oder Migrationsskript!** `DROP TABLE IF EXISTS` entfernt vorhandene Tabellen samt Daten. `USE hotel` richtet den Import ausdrücklich auf die Datenbank `hotel`, unabhängig von der vorherigen Auswahl in phpMyAdmin. Nur für eine neue Demo-Installation verwenden. Wenn `hotel` bereits Daten enthält, nicht importieren: zuerst sichern und eine gezielte Lösung für das bestehende Schema planen.

## 4.1 SQL-Datei auswählen

Vorher in phpMyAdmin prüfen, ob `hotel` bereits existiert. Nur fortfahren, wenn die Datenbank noch nicht existiert oder nachweislich leer ist und ausschließlich für diese Demo vorgesehen ist.

In phpMyAdmin:

1. Den Bereich **Importieren** öffnen.
2. Die Datei `Nest/Database/hotel_deutsch.sql` auswählen.
3. Den Import nur unter den oben genannten Voraussetzungen starten und die Rückmeldung auf Fehler prüfen.

Die SQL-Datei enthält unter anderem:

```sql
CREATE DATABASE IF NOT EXISTS `hotel` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
USE `hotel`;
```

Dadurch wird die Datenbank `hotel` erstellt, falls sie noch nicht existiert. `IF NOT EXISTS` schützt jedoch nicht die enthaltenen Tabellen: Diese werden im weiteren Skript durch `DROP TABLE IF EXISTS` gelöscht und neu angelegt.

---

## 4.2 Datenbank überprüfen

Nach dem Import sollte in phpMyAdmin die Datenbank

```text
hotel
```

vorhanden sein.

Die Datenbank enthält folgende elf Tabellen:

```text
benutzer
zimmer
gaeste
reservierungen
dienstleistungen
dienstleistungsbestellungen
autovermietungen
taxibestellungen
restaurant_reservierungen
veranstaltungen
ticketbestellungen
```

Prüfen, ob alle elf Tabellen vorhanden sind und der Import keine Fehler gemeldet hat. Anschließend die Verbindung und Anmeldung wie unten beschrieben testen. Das bloße Vorhandensein der Tabellennamen bestätigt noch nicht die vollständige Übereinstimmung der Spalten.

---

# 5. Nest-Projekt in Visual Studio öffnen

Nach der Einrichtung der Datenbank wird das Projekt gestartet.

1. **Visual Studio 2022** öffnen.
2. Die Datei

```text
Nest.sln
```

öffnen.
3. Warten, bis das Projekt vollständig geladen wurde.
4. Prüfen, ob alle erforderlichen NuGet-Pakete geladen wurden.

Das Projekt verwendet unter anderem:

* .NET 8
* WPF
* Caliburn.Micro
* MySql.Data

Voraussetzung ist Windows mit dem .NET-8-SDK und der Visual-Studio-Workload **.NET-Desktopentwicklung**.

---

# 6. Datenbankverbindung prüfen

Die Datenbankverbindung ist im Projekt in der Klasse

```text
Nest.Data.DB
```

definiert.

Die Anwendung verwendet folgende Verbindungsdaten:

```text
Server:     localhost
Port:       3306
Database:   hotel
User:       root
Password:   root
SSL:        Disabled
```

Die entsprechende Verbindungszeichenfolge lautet:

```text
server=localhost;port=3306;database=hotel;uid=root;pwd=root;SslMode=Disabled;
```

Vor dem Start des Programms muss deshalb sichergestellt werden, dass:

* Der MySQL-/MariaDB-Dienst in XAMPP läuft.
* MySQL auf Port `3306` läuft.
* Die Datenbank `hotel` existiert.
* Die aktuelle deutsche Tabellenstruktur vorhanden ist (bei einer neuen Demo-Installation durch den einmaligen Import aus Abschnitt 4).

Apache ist nur erforderlich, wenn zusätzlich phpMyAdmin verwendet wird. Die oben genannten Werte entsprechen `Nest/Data/DB.cs` und setzen voraus, dass der lokale Datenbankserver diesen Zugang bereits akzeptiert. Diese Anleitung ändert weder das Datenbankpasswort noch die Verbindungseinstellungen; bei abweichenden lokalen Zugangsdaten nicht die Datenbank neu importieren.

---

# 7. Programm starten

Wenn die Datenbank eingerichtet wurde und das Projekt in Visual Studio geöffnet ist:

1. Projekt erstellen:

```text
Build → Build Solution
```

oder:

```text
Strg + Shift + B
```

2. Anschließend das Programm mit

```text
F5
```

starten.

Das Programm wird nun gestartet und die Login-Seite wird angezeigt.

---

# 8. Anmeldung

Für den ersten Test sind nach der Demo-Erstinstallation die folgenden zwei Benutzer vorgesehen. Diese veröffentlichten Demo-Zugänge sind nur für eine lokale Demonstration bestimmt, nicht für einen produktiven oder öffentlich erreichbaren Betrieb.

## Administrator

```text
Benutzername: admin
Passwort:     admin123
```

Der Administrator hat Zugriff auf die Verwaltungsfunktionen.

## Rezeptionist

```text
Benutzername: receptionist
Passwort:     rec123
```

Der Rezeptionist hat Zugriff auf die Funktionen für den Empfang und die Reservierungsverwaltung.

---

# 9. Beispiel-Daten

Beim Import der SQL-Datei werden automatisch Testdaten angelegt.

## Benutzer

* `admin`
* `receptionist`

Beide vorgesehenen Demo-Konten haben den Status `Aktiv`. Das Skript enthält außerdem einen älteren Benutzer-Datensatz mit gesetztem `geloescht_am`; dieser gehört nicht zu den vorgesehenen Demo-Zugängen.

## Zimmer

Es werden acht Beispielzimmer angelegt:

* 101
* 102
* 201
* 202
* 301
* 302
* 401
* 402

Die Zimmer besitzen unterschiedliche Kategorien, Kapazitäten und Preise.

## Dienstleistungen

Beispielsweise werden folgende Dienstleistungen angelegt:

* Fruehstuecksbuffet
* Zimmerservice-Mahlzeit
* Waesche (pro kg)
* Chemische Reinigung
* Wellness - Ganzkoerperbehandlung
* Wellness - Massage
* Flughafentransfer
* Stadtrundfahrt

## Veranstaltungen

Zusätzlich werden vier Beispielveranstaltungen mit festen Terminen vom 15. bis 17. September 2026 angelegt. Liegen diese Termine bereits in der Vergangenheit, erscheinen sie nicht mehr in der Liste der bevorstehenden Veranstaltungen im Kundenportal. Für eine aktuelle Demonstration über die Veranstaltungsverwaltung einen zukünftigen Termin anlegen; eine leere Liste ist kein Grund für einen erneuten SQL-Import.

## Gäste und Reservierungen

Das Skript enthält einen Gast und eine Beispielreservierung für Zimmer 402 vom 18. bis 20. September 2026 mit Status `Reserviert`. Die festen Beispieldaten passen sich nicht automatisch an das aktuelle Datum an. Für neue Testbuchungen einen passenden zukünftigen Zeitraum wählen.

---

# 10. Zimmerreservierung

Bei einer Zimmerreservierung werden unter anderem folgende Daten angegeben:

* Gast
* Anreisedatum
* Abreisedatum
* Anzahl der Erwachsenen
* Anzahl der Kinder
* Zimmer
* Zahlungsinformationen

Die verfügbaren Zimmer werden automatisch anhand des ausgewählten Zeitraums geprüft.

Die zeitabhängige Suche in `RoomRepository.GetAvailableRooms(DateTime, DateTime)` berücksichtigt ein Zimmer nur, wenn:

1. der Zimmerstatus `Verfuegbar`, `Reserviert` oder `Belegt` ist,
2. das Zimmer nicht als gelöscht markiert ist (`geloescht_am IS NULL`) und
3. keine Reservierung mit Status `Reserviert` oder `Eingecheckt` den ausgewählten Zeitraum überschneidet.

Reservierungen mit dem Status `Storniert` oder `Ausgecheckt` blockieren diese Suche nicht. Der Abreisetag einer bestehenden Reservierung darf zugleich der Anreisetag einer neuen Reservierung sein. Zimmer mit Status `Wartung` werden ausgeschlossen. Auch ein derzeit belegtes oder reserviertes Zimmer kann für einen anderen, freien Zeitraum angeboten werden. Die zusätzliche Repository-Methode ohne Datumsparameter berücksichtigt dagegen nur Zimmer mit Status `Verfuegbar`, die nicht als gelöscht markiert sind.

---

# 11. Zimmerstatus und Reservierungen

Ein Zimmer kann folgende Status besitzen:

```text
Verfuegbar
Belegt
Wartung
Reserviert
```

Der Zimmerstatus beschreibt den allgemeinen Zustand des Zimmers.

Die Reservierung beschreibt dagegen einen konkreten Aufenthalt eines Gastes mit einem bestimmten Zeitraum.

Die Auswahl eines anderen Datums verändert daher nicht direkt den gespeicherten Zimmerstatus.

Stattdessen wird bei der Suche geprüft, ob das Zimmer für den gewünschten Zeitraum verfügbar ist.

### Beispiel

Ein Zimmer besitzt den Status:

```text
Verfuegbar
```

Für dieses Zimmer existiert jedoch eine Reservierung mit Status `Reserviert` vom (schematisches Datumsbeispiel):

```text
20.09.2026 – 25.09.2026
```

Wenn nach einem Zimmer für den Zeitraum

```text
21.09.2026 – 23.09.2026
```

gesucht wird, wird dieses Zimmer nicht als verfügbar angezeigt.

Für einen nicht überlappenden Zeitraum kann das Zimmer wieder angeboten werden, sofern die oben genannten Bedingungen erfüllt sind. Die Reservierungsstatus lauten `Reserviert`, `Eingecheckt`, `Ausgecheckt` und `Storniert`; sie sind vom Zimmerstatus zu unterscheiden.

---

# 12. Fehlerbehebung

## MySQL startet nicht

Überprüfen:

* Ist XAMPP geöffnet?
* Läuft der MySQL-Dienst?
* Wird Port `3306` verwendet?

Falls ein anderer Dienst bereits Port `3306` verwendet, kann die Datenbankverbindung des Projekts nicht wie vorgesehen hergestellt werden.

---

## phpMyAdmin kann nicht geöffnet werden

Überprüfen, ob Apache in XAMPP gestartet wurde.

Danach erneut öffnen:

```text
http://localhost/phpmyadmin
```

---

## Datenbank `hotel` fehlt

Zuerst prüfen, ob phpMyAdmin und die Anwendung denselben lokalen Datenbankserver verwenden (Host `localhost`, Port `3306`). Eine fehlende Datenbank kann auch auf die Verbindung zum falschen Server hindeuten.

Nur wenn tatsächlich eine neue Demo-Installation eingerichtet werden soll und keine bestehenden Daten betroffen sind, die Erstinstallation aus Abschnitt 4 durchführen. Nicht blind erneut importieren und keine bestehende Datenbank löschen.

---

## Tabellen fehlen

Die Datenbank `hotel` in phpMyAdmin öffnen und überprüfen, ob folgende Tabellen vorhanden sind:

```text
benutzer
zimmer
gaeste
reservierungen
dienstleistungen
dienstleistungsbestellungen
autovermietungen
taxibestellungen
restaurant_reservierungen
veranstaltungen
ticketbestellungen
```

Falls Tabellen fehlen oder Fehler wie `Unknown column` auftreten, die genaue Fehlermeldung festhalten und die vorhandene Struktur mit `Nest/Database/hotel_deutsch.sql` und den Repository-Abfragen vergleichen. Englische Tabellennamen können auf das ältere Schema hindeuten. Bei vorhandenen Daten zuerst sichern und eine gezielte Schemaanpassung planen. **Keinen pauschalen Neuimport durchführen:** Er würde auch Daten in den bereits vorhandenen Tabellen löschen.

---

## Keine Verbindung zur Datenbank

Die Verbindungseinstellungen in `DB.cs` überprüfen:

```text
Server:     localhost
Port:       3306
Database:   hotel
User:       root
Password:   root
```

Außerdem muss der MySQL-Dienst in XAMPP laufen.

---

## Login funktioniert nicht

Für den ersten Test können folgende Zugangsdaten verwendet werden:

```text
Administrator:
admin / admin123
```

oder:

```text
Rezeptionist:
receptionist / rec123
```

In phpMyAdmin kann zusätzlich überprüft werden, ob die vorgesehenen Demo-Benutzer in der Tabelle `benutzer` vorhanden sind und den Status `Aktiv` haben. Die Rollen heißen `Admin` und `Rezeptionist`; der weitere Benutzerstatus ist `Inaktiv`. Ein Login-Fehler ist kein Anlass für einen erneuten SQL-Import.

---

# 13. Projektstruktur

Das Projekt ist nach dem **MVVM-Prinzip** aufgebaut.

Die wichtigsten Bereiche sind:

```text
Repository-Stamm
├── Nest
│   ├── Data
│   │   ├── DB.cs
│   │   └── Repository-Klassen
│   ├── Database
│   │   └── hotel_deutsch.sql
│   ├── Models
│   ├── ViewModels
│   │   └── Admin
│   └── Views
│       └── Admin
└── Nest.sln
```

## Data

Enthält die Datenbankverbindung und Repository-Klassen für den Datenzugriff.

## Models

Enthält die Datenmodelle, beispielsweise für Benutzer, Zimmer, Gäste und Reservierungen.

## ViewModels

Enthält die Anwendungslogik und die Verbindung zwischen Benutzeroberfläche und Datenzugriff.

## Views

Enthält die grafischen Benutzeroberflächen der Anwendung.

## ViewModels/Admin und Views/Admin

Enthalten die Verwaltungsfunktionen für den Administrator und deren Oberflächen.

---

# 14. Vollständiger Startablauf

Bei einem erneuten Start des Projekts reicht normalerweise folgende Reihenfolge:

```text
XAMPP starten
      ↓
MySQL starten
      ↓
Optional: Apache starten und phpMyAdmin öffnen
      ↓
Bei Bedarf: Datenbank "hotel" überprüfen (kein Neuimport)
      ↓
Visual Studio öffnen
      ↓
Nest.sln öffnen
      ↓
Projekt erstellen
      ↓
F5
      ↓
Login
```

Die SQL-Datei darf **nicht als routinemäßiger Start- oder Reparaturschritt erneut importiert werden**. Sie ist ausschließlich für die Erstinstallation einer neuen Demo-Datenbank vorgesehen; bei vorhandenen Daten gelten die Warnungen aus Abschnitt 4. Für den normalen WPF-Betrieb genügt MySQL/MariaDB, Apache und phpMyAdmin sind optional.

---

# 15. Beenden des Programms

Nach der Verwendung kann das Programm normal geschlossen werden.

Wenn XAMPP nicht mehr benötigt wird, können anschließend Apache und MySQL über das XAMPP Control Panel beendet werden.
