# STARTANLEITUNG – Nest Hotel Management System

## Schnellstart

Für den Start des Programms sind folgende Schritte erforderlich:

```text
1. XAMPP installieren
        ↓
2. Apache + MySQL starten
        ↓
3. http://localhost/phpmyadmin öffnen
        ↓
4. SQL-Datei importieren
        ↓
5. Nest-Projekt in Visual Studio öffnen
        ↓
6. Datenbankverbindung prüfen
        ↓
7. F5 → Programm starten
```

---

# 1. XAMPP installieren

Für das Projekt wird eine lokale MySQL-Datenbank benötigt.

Dafür wird **XAMPP** verwendet.

Nach der Installation von XAMPP wird das **XAMPP Control Panel** geöffnet.

---

# 2. Apache + MySQL starten

Im XAMPP Control Panel müssen folgende Dienste gestartet werden:

* **Apache**
* **MySQL**

Beide Dienste müssen erfolgreich gestartet sein.

Für das Projekt ist insbesondere **MySQL** erforderlich, da die Anwendung auf eine lokale MySQL-Datenbank zugreift.

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

Die SQL-Datei des Projekts enthält die vollständige Datenbankstruktur und die benötigten Testdaten.

## 4.1 SQL-Datei auswählen

In phpMyAdmin:

1. Den Bereich **Importieren** öffnen.
2. Die SQL-Datei des Projekts auswählen.
3. Den Import starten.

Die SQL-Datei enthält unter anderem:

```sql
CREATE DATABASE IF NOT EXISTS hotel;
USE hotel;
```

Dadurch wird die Datenbank `hotel` erstellt, falls sie noch nicht existiert.

---

## 4.2 Datenbank überprüfen

Nach dem Import sollte in phpMyAdmin die Datenbank

```text
hotel
```

vorhanden sein.

Die Datenbank enthält folgende Tabellen:

```text
users
rooms
guests
reservations
services
service_orders
events
ticket_orders
```

Wenn diese Tabellen vorhanden sind, wurde die Datenbank erfolgreich eingerichtet.

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

Die entsprechende Connection String lautet:

```text
server=localhost;port=3306;database=hotel;uid=root;pwd=root;SslMode=Disabled;
```

Vor dem Start des Programms muss deshalb sichergestellt werden, dass:

* XAMPP gestartet ist.
* Apache läuft.
* MySQL läuft.
* MySQL auf Port `3306` läuft.
* Die Datenbank `hotel` existiert.
* Die SQL-Datei erfolgreich importiert wurde.

Wenn diese Voraussetzungen erfüllt sind, muss die `DB.cs` normalerweise nicht verändert werden.

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

Für den ersten Test stehen zwei Benutzer zur Verfügung.

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

* Breakfast Buffet
* Room Service Meal
* Laundry
* Dry Cleaning
* Spa
* Airport Transfer
* City Tour

## Veranstaltungen

Zusätzlich werden vier Beispielveranstaltungen angelegt.

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

Ein Zimmer wird nur als verfügbar angezeigt, wenn:

1. der Zimmerstatus `Available` ist und
2. keine aktive Reservierung für den ausgewählten Zeitraum existiert.

Reservierungen mit dem Status `Cancelled` werden bei der Verfügbarkeitsprüfung nicht berücksichtigt.

---

# 11. Zimmerstatus und Reservierungen

Ein Zimmer kann folgende Status besitzen:

```text
Available
Occupied
Maintenance
Reserved
```

Der Zimmerstatus beschreibt den allgemeinen Zustand des Zimmers.

Die Reservierung beschreibt dagegen einen konkreten Aufenthalt eines Gastes mit einem bestimmten Zeitraum.

Die Auswahl eines anderen Datums verändert daher nicht direkt den gespeicherten Zimmerstatus.

Stattdessen wird bei der Suche geprüft, ob das Zimmer für den gewünschten Zeitraum verfügbar ist.

### Beispiel

Ein Zimmer besitzt den Status:

```text
Available
```

Für dieses Zimmer existiert jedoch eine Reservierung vom:

```text
20.09.2026 – 25.09.2026
```

Wenn nach einem Zimmer für den Zeitraum

```text
21.09.2026 – 23.09.2026
```

gesucht wird, wird dieses Zimmer nicht als verfügbar angezeigt.

Nach dem Ende der Reservierung kann das Zimmer wieder für einen anderen Zeitraum ausgewählt werden, sofern sein allgemeiner Status weiterhin `Available` ist.

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

Die SQL-Datei erneut über phpMyAdmin importieren.

Anschließend prüfen, ob die Datenbank `hotel` vorhanden ist.

---

## Tabellen fehlen

Die Datenbank `hotel` in phpMyAdmin öffnen und überprüfen, ob folgende Tabellen vorhanden sind:

```text
users
rooms
guests
reservations
services
service_orders
events
ticket_orders
```

Falls Tabellen fehlen, sollte die SQL-Datei erneut vollständig importiert werden.

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

In phpMyAdmin kann zusätzlich überprüft werden, ob die Benutzer in der Tabelle `users` vorhanden und auf `Active` gesetzt sind.

---

# 13. Projektstruktur

Das Projekt ist nach dem **MVVM-Prinzip** aufgebaut.

Die wichtigsten Bereiche sind:

```text
Nest
├── Data
│   ├── DB.cs
│   └── Repository-Klassen
│
├── Models
│   └── Datenmodelle
│
├── ViewModels
│   └── Anwendungslogik
│
├── Views
│   └── Benutzeroberflächen
│
├── Admin
│   └── Verwaltungsfunktionen
│
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

## Admin

Enthält die Verwaltungsfunktionen für den Administrator.

---

# 14. Vollständiger Startablauf

Bei einem erneuten Start des Projekts reicht normalerweise folgende Reihenfolge:

```text
XAMPP starten
      ↓
Apache + MySQL starten
      ↓
phpMyAdmin öffnen
      ↓
Datenbank "hotel" überprüfen
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

Die SQL-Datei muss **nicht bei jedem Programmstart erneut importiert werden**. Sie wird nur benötigt, wenn die Datenbank neu eingerichtet werden muss.

---

# 15. Beenden des Programms

Nach der Verwendung kann das Programm normal geschlossen werden.

Wenn XAMPP nicht mehr benötigt wird, können anschließend Apache und MySQL über das XAMPP Control Panel beendet werden.
