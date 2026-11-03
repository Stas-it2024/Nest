# Nest – Grand Horizon Hotel Management System

Nest ist eine Windows-Desktop-Anwendung zur Hotelverwaltung mit C#, .NET 8 und WPF. Sie verbindet einen Administrationsbereich, eine Rezeption und ein Kundenportal mit einer gemeinsamen MySQL-/MariaDB-Datenbank.

Das Projekt entstand als schulische Teamarbeit am bib International College in Paderborn und wird anschließend von Stanislav Kharchenko als Portfolio-Projekt individuell weiterentwickelt. Die ursprünglichen Teambeiträge und die nachfolgenden Verbesserungen sind unten getrennt dokumentiert.

**Projektstatus:** lokale Lern- und Portfolio-Demonstration, keine produktionsreife Hotelsoftware. Insbesondere Authentifizierung, Konfiguration und automatisierte Tests benötigen weitere Arbeit. Im Kundenportal werden keine realen Zahlungen ausgeführt.

## Funktionen der aktuellen Anwendung

### Anmeldung und Zugang

- Mitarbeiteranmeldung mit Benutzername und Passwort; Navigation entsprechend den Rollen `Admin` und `Rezeptionist`.
- Kundenportal ohne Gastkonto, erreichbar über die Anmeldeseite und aus der Rezeption.
- Mitarbeiterkonten werden in der Benutzerverwaltung angelegt; eine öffentliche Selbstregistrierung ist nicht implementiert.

### Administration

| Bereich | Vorhandene Funktionen |
| --- | --- |
| Dashboard | Kennzahlen aus der Datenbank, Zimmerstatusübersicht und Reservierungsliste |
| Zimmerverwaltung | Zimmer anzeigen, anlegen, bearbeiten und als gelöscht markieren; Typ, Kapazität, Preis und Status verwalten |
| Benutzerverwaltung | Mitarbeiterkonten, Rollen und Aktivstatus verwalten; Benutzer als gelöscht markieren |
| Dienstleistungen | Dienstleistungskatalog mit Kategorien, Preisen und Verfügbarkeitsstatus verwalten |
| Veranstaltungen | Veranstaltungen mit Termin, Ort, Ticketpreis und Ticketkontingent anlegen, bearbeiten und löschen |
| Alle Reservierungen | Gemeinsame Übersicht für Zimmerreservierungen und Ticketbestellungen; Suche und Statusfilter für Zimmerreservierungen |

### Rezeption

- Reservierungen mit Gastdaten, Aufenthaltszeitraum, Personenzahl, Zahlungsart und Anzahlung erfassen.
- Check-in, Check-out und Stornierung mit Aktualisierung der zugehörigen Zimmerstatus durchführen.
- Gäste suchen und ihren Aufenthaltsverlauf anzeigen.
- Zimmerreservierungen und Ticketbestellungen in getrennten Tabellen einsehen.
- Das bestehende Kundenportal innerhalb des Rezeptionsbereichs öffnen.

### Kundenportal

| Bereich | Vorhandene Funktionen |
| --- | --- |
| Zimmer buchen | Verfügbarkeit für den gewählten Zeitraum abfragen, Zimmerinformationen und Vorschaubilder anzeigen, Preis berechnen und Reservierung mit Buchungscode speichern |
| Auto mieten | Mietanfrage mit Fahrzeugkategorie, Zeitraum, Kontaktdaten und berechnetem Preis speichern |
| Taxi bestellen | Anfrage mit Name, Abholort, Datum, Uhrzeit und Fahrzeugtyp speichern |
| Restaurant | Tischreservierung mit Gästezahl, Datum, Uhrzeit, Anlass und Sonderwünschen speichern |
| Veranstaltungen und Tickets | Bevorstehende Veranstaltungen laden und Ticketbestellungen mit Ticketcode speichern; Kontingentprüfung und Bestandsaktualisierung erfolgen im Repository |
| Online-Zahlung | Reservierung über ihren Code laden, eine lokale Demo-Zahlung erfassen und einen PDF-Zahlungsbeleg erzeugen bzw. öffnen |

**Die Online-Zahlung ist ausschließlich eine Simulation.** Es gibt keinen angebundenen Zahlungsanbieter und keine reale Banktransaktion. Die Simulation verändert den gespeicherten Zahlungsstand der Demo-Reservierung. Der PDF-Beleg ist kein Nachweis einer tatsächlichen Zahlung. Nur fiktive Bank- und Gästedaten verwenden.

Auto- und Taxianfragen werden lokal gespeichert; daraus folgt keine Beauftragung eines externen Anbieters.

## Technologien und Aufbau

Die Konfiguration ist in [Nest.csproj](Nest/Nest.csproj) hinterlegt:

| Bestandteil | Verwendung |
| --- | --- |
| C# und .NET 8 | Zielplattform `net8.0-windows` |
| WPF / XAML | Windows-Oberflächen, Data Binding, Styles und ResourceDictionaries |
| Caliburn.Micro 5.0.258 | MVVM-Konventionen, Navigation und Änderungsbenachrichtigungen |
| MVVM und Repository-Klassen | Views, ViewModels und gekapselte Datenzugriffe; einzelne ViewModels enthalten weiterhin direkte SQL-Abfragen |
| MySQL / MariaDB | Relationale Datenbank `hotel`; lokale Einrichtung über XAMPP beschrieben, keine konkrete Serverversion im Projekt festgelegt |
| MySql.Data 9.7.0 | Der im Anwendungscode verwendete ADO.NET-Datenbanktreiber |
| PDFsharp-MigraDoc-WPF 6.2.4 | Erzeugung der PDF-Demo-Zahlungsbelege |
| Visual Studio 2022 | Entwicklungsumgebung mit .NET-Desktopentwicklung |

`MySqlConnector` und `System.Data.SQLite` sind zusätzlich noch als Pakete referenziert. Ihre Notwendigkeit ist vor einer späteren Bereinigung zu prüfen; der aktuelle Datenzugriff verwendet `MySql.Data`, nicht SQLite.

Die Anwendung nutzt parametrisierte SQL-Abfragen sowie Transaktionen und Zeilensperren in Buchungsabläufen. Das sind vorhandene Implementierungsmechanismen, keine Zusicherung vollständiger Sicherheit oder Fehlerfreiheit. Die Geschäftslogik liegt überwiegend in ViewModels und Repositories; eine vollständige zusätzliche Serviceschicht wird nicht behauptet.

```text
Nest.sln
Nest/
├── Data/          Datenbankverbindung und Repositories
├── Database/      Aktuelles deutsches Installationsskript
├── Models/        Daten- und Anzeigemodelle
├── ViewModels/    Administration, Rezeption, Kundenportal und Navigation
├── Views/         Zugehörige WPF-Oberflächen
├── Services/      PDF-Erzeugung
├── Styles/        Gemeinsam genutzte XAML-Styles und Farben
├── Converters/    Konvertierung von Binding-Werten für die Darstellung
└── Assets/Rooms/  Zimmerbilder
```

## Lokal starten

Die verbindliche Schritt-für-Schritt-Anleitung steht in [STARTANLEITUNG.md](STARTANLEITUNG.md). Sie beschreibt XAMPP, phpMyAdmin, die bestehenden Verbindungseinstellungen und den Start in Visual Studio.

1. Windows, Visual Studio 2022 mit der Workload **.NET-Desktopentwicklung** und das .NET-8-SDK bereitstellen.
2. Den MySQL-/MariaDB-Dienst in XAMPP starten. Apache wird zusätzlich für phpMyAdmin benötigt; die WPF-Anwendung verbindet sich direkt mit der Datenbank und benötigt Apache nicht.
3. Für eine neue lokale Demo-Installation die Datenbank nach der Startanleitung einrichten. Vor jedem Import die dortigen Warnungen beachten.
4. `Nest.sln` öffnen, NuGet-Pakete wiederherstellen, die Projektmappe erstellen und mit `F5` starten.

Die aktuelle Verbindung ist in [Nest/Data/DB.cs](Nest/Data/DB.cs) fest hinterlegt. Die vorhandenen Einstellungen werden durch diese Dokumentation nicht geändert.

### Demo-Anmeldung

Diese veröffentlichten Zugangsdaten gehören zur lokalen Demo-Installation und dürfen nicht für reale Konten wiederverwendet werden:

| Rolle | Benutzername | Passwort |
| --- | --- | --- |
| Administrator | `admin` | `admin123` |
| Rezeptionist | `receptionist` | `rec123` |

Das Kundenportal benötigt kein Gastkonto.

## Aktuelle Datenbank

Die aktuelle deutsche Schema- und Demodaten-Datei ist [Nest/Database/hotel_deutsch.sql](Nest/Database/hotel_deutsch.sql). Sie ist für die **Erstinstallation einer neuen Demo-Datenbank** vorgesehen, nicht als Reparatur- oder Migrationsskript.

**Achtung – Datenverlust:** Das Skript enthält `USE hotel` (im SQL mit Backticks geschrieben) und `DROP TABLE IF EXISTS`. Ein erneuter Import löscht die vorhandenen Daten in den elf enthaltenen Tabellen von `hotel` und legt diese Tabellen mit Demodaten neu an. Die Auswahl einer anderen Datenbank in phpMyAdmin schützt davor nicht. Bei bestehenden Daten nicht erneut importieren; zuerst sichern und eine gezielte Schemaanpassung planen.

Die ältere Datei [hotel_database.sql](hotel_database.sql) liegt noch im Repository, enthält aber die frühere englische Struktur und ist **nicht für die aktuelle Anwendung zu verwenden**.

| Tabelle | Inhalt |
| --- | --- |
| `benutzer` | Mitarbeiterkonten, Rollen und Aktivstatus |
| `zimmer` | Zimmer, Kategorien, Kapazitäten, Preise und Status |
| `gaeste` | Gäste und Kontaktdaten |
| `reservierungen` | Zimmerreservierungen, Aufenthaltszeiträume und Zahlungsstand |
| `dienstleistungen` | Dienstleistungskatalog |
| `dienstleistungsbestellungen` | Zuordnung von Dienstleistungen zu Reservierungen |
| `autovermietungen` | Mietanfragen und berechnete Preise |
| `taxibestellungen` | Taxianfragen und Abholinformationen |
| `restaurant_reservierungen` | Tischreservierungen |
| `veranstaltungen` | Veranstaltungen und Ticketkontingente |
| `ticketbestellungen` | Ticketbestellungen und Ticketcodes |

Zimmerstatus: `Verfuegbar`, `Belegt`, `Wartung`, `Reserviert`. Reservierungsstatus: `Reserviert`, `Eingecheckt`, `Ausgecheckt`, `Storniert`. Diese Werte sind von englischen C#-Bezeichnern zu unterscheiden.

Die Demodaten enthalten feste Veranstaltungs- und Reservierungstermine im September 2026 sowie ältere Entwicklungsdatensätze. Vergangene Veranstaltungen erscheinen nicht in der Liste bevorstehender Veranstaltungen. Für eine aktuelle Vorführung einen zukünftigen Termin über die Administration anlegen, statt die Datenbank erneut zu importieren.

## Herkunft und Beiträge

### Ursprüngliche Teamarbeit

Die ursprüngliche Anwendung entstand in der Klasse PBT3H24A am bib International College in Paderborn. Die folgende Zuordnung stammt aus Kapitel 2 der [ursprünglichen Projektdokumentation](Dokumentation/Projektdokumentation_Grand_Horizon_Hotel.docx); sie beschreibt die dokumentierte Teamaufteilung und keine nachträgliche Alleinautorschaft.

| Teammitglied | Dokumentierter ursprünglicher Beitrag |
| --- | --- |
| Stanislav Kharchenko | Projektstruktur und MVVM mit Caliburn.Micro; Start- und Login-Seite, Rollenerkennung, Zimmer- und Benutzerverwaltung, alle Reservierungen im Admin-Bereich sowie Integration der Projektteile |
| Ismail Faiz | Admin-Dashboard, Dienstleistungsverwaltung, Veranstaltungsverwaltung und SQL-Datei für die Datenbank |
| Tasnim Alloufi | Reservierungsverwaltung, Check-in/Check-out, Gästeliste mit Aufenthaltsverlauf und Übersicht aller Buchungen im Rezeptionsbereich |
| Khadija Ez-Zarradi | Kundenportal mit Zimmerbuchung, Auto-, Taxi- und Restaurantfunktionen, Veranstaltungen und Tickets, Demo-Online-Zahlung und PDF-Beleg |

### Nachfolgende individuelle Weiterentwicklung

Stanislav Kharchenko führt das Projekt auf dieser gemeinsamen Grundlage weiter. Folgende spätere Änderungen sind in der Git-Historie unter dem Autor `Stas-it2024` nachvollziehbar:

| Commit | Nachweisbare Änderung |
| --- | --- |
| [2c5c94f](https://github.com/Stas-it2024/Nest/commit/2c5c94fe0f66610837b69b20106332389e4e2661) | Überarbeitung des Kundenportals und der Begrüßungsbindung, gemeinsame Button-Styles sowie Verbesserungen der Reservierungskonsistenz: gemeinsame Speicherung von Gast und Reservierung in einer Transaktion, Kapazitäts- und Eingabeprüfung sowie überarbeitete Verfügbarkeitsbedingungen |
| [4b52a35](https://github.com/Stas-it2024/Nest/commit/4b52a35e8f06bde995a1a738e75fadc131bd6185) | Extraktion wiederkehrender Farben und WPF-Styles in ResourceDictionaries und Wiederverwendung in Login, Administration, Rezeption und Kundenportal |
| [ab9cd40](https://github.com/Stas-it2024/Nest/commit/ab9cd40908e4200050e8ffc9a26c84d85f0bf85e) | Aktualisierung der deutschen Startanleitung für die aktuelle Datenbankstruktur einschließlich Warnung vor Datenverlust beim erneuten Import |

Diese Auswahl dokumentiert spätere Änderungen, nicht die ursprüngliche Urheberschaft sämtlicher betroffener Funktionen. Die Teambeiträge bleiben davon unberührt.

## Bekannte Grenzen und nächste Schritte

- **Keine sichere Produktionsauthentifizierung:** Passwörter werden derzeit im Klartext gespeichert und verglichen. Rollen steuern die Navigation, ersetzen aber kein umfassendes Autorisierungskonzept. Vor einem realen Einsatz sind unter anderem Passwort-Hashing, konsistente Kontosperren und Berechtigungsprüfungen erforderlich.
- **Lokale Demo-Konfiguration:** Die Datenbankverbindung enthält fest hinterlegte Zugangsdaten, nutzt `root` und deaktiviert SSL. Vor einem anderen Einsatz sind eine externe Konfiguration, ein eingeschränktes Datenbankkonto und ein passendes Transport- und Zugriffsschutzkonzept nötig.
- **Gastzugriff ohne Identitätsnachweis:** Die Demo-Zahlung lädt Reservierungen über den Buchungscode; das ersetzt keine Prüfung, ob eine Person auf die Reservierung zugreifen darf.
- **Keine automatisierte Testsuite im Repository:** Die vorhandene Testdokumentation ersetzt keine ausführbaren Unit- und Integrationstests. Priorität haben Datumsüberschneidungen, Kapazitätsgrenzen, parallele Buchungen und Ticketkäufe, Zahlungsstände sowie Check-in/Check-out und Stornierung.
- **Weitere technische Bereinigung:** Synchrone Datenbankzugriffe können die Oberfläche blockieren. Direkte SQL-Zugriffe in einzelnen ViewModels, verbliebene Abhängigkeiten und uneinheitliche technische Bezeichner sollen schrittweise überprüft werden.
- **Keine Zusage zur Produktionsreife:** Es wird weder vollständige Fehlerfreiheit noch eine Sicherheits-, Datenschutz- oder Lastprüfung behauptet. Ausschließlich lokal mit geeigneten Testdaten demonstrieren; keine echten Gäste-, Bank- oder wiederverwendeten Zugangsdaten einsetzen.

## Ursprüngliche Projektdokumentation

Die ursprünglichen Dokumente bleiben als Nachweis der Teamarbeit und des damaligen Projektstands erhalten:

- [Projektdokumentation als PDF](Dokumentation/Projektdokumentation_Grand_Horizon_Hotel.pdf)
- [Projektdokumentation als DOCX](Dokumentation/Projektdokumentation_Grand_Horizon_Hotel.docx)
- [Testdokumentation](Dokumentation/Testdokumentation.docx)
- [Pflichtenheft](Pflichtenheft/Pflichtenheft-Hotelmangment.pdf)

Darin enthaltene ältere Schemaangaben und Qualitätsaussagen sind historisch einzuordnen. Für die aktuelle Einrichtung gilt die [Startanleitung](STARTANLEITUNG.md); Aussagen über automatisierte Tests oder heutige Produktionsreife lassen sich aus diesen Dokumenten nicht ableiten.
