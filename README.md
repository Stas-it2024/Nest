# Grand Horizon Hotel Management System

Ein vollständiges **Hotelverwaltungssystem** entwickelt mit **C#, WPF und MySQL** — eine Desktop-Anwendung für Hotelmitarbeiter und Gäste.

---

## Funktionen

### Anmeldung & Zugang
- Mitarbeiter-Login mit rollenbasiertem Zugriff (**Admin** / **Rezeptionist**)
- Kontoregistrierung für neue Mitarbeiter
- **Gästeportal** 

### Admin-Bereich

| Modul | Beschreibung |
|-------|-------------|
| **Dashboard** | 8 Live-Statistikkarten (Zimmer, Belegung, Umsatz, Gäste), farbcodierter Zimmerstatus, letzte Buchungen |
| **Zimmerverwaltung** | Zimmer hinzufügen / bearbeiten / löschen (Standard, Deluxe, Suite, Executive) mit Preis, Kapazität und Status |
| **Benutzerverwaltung** | Mitarbeiterkonten, Rollen und aktiv/inaktiv Status verwalten |
| **Dienstleistungen** | Hotelservices-Katalog mit Preisen (Spa, Wäsche, Speisen, Transport) |
| **Event-Verwaltung** | Hotel-Events erstellen mit Ticketpreis und maximaler Teilnehmerzahl |
| **Alle Reservierungen** | Vollständige Übersicht aller Gästebuchungen und Ticket-Bestellungen mit Suche und Filter |

### Rezeptionisten-Bereich

| Modul | Beschreibung |
|-------|-------------|
| **Reservierungen** | Reservierung erstellen, Check-in / Check-out / Stornierung, automatische Zimmerstatusänderung |
| **Gästeverwaltung** | Vollständige Gästeliste mit Aufenthaltsverlauf pro Gast |
| **Alle Buchungen** | Alle Reservierungen mit Statusfilter und Suchfunktion |
| **Kundenportal** | Direktzugang zum Gästeportal von der Rezeption aus |

### Kundenportal (Gäste)

Gäste öffnen das Portal über den Hauptlogin-Bildschirm — **kein Konto erforderlich**.

| Tab | Funktion |
|-----|---------|
| **Zimmer buchen** | Verfügbare Zimmer mit Live-Preisen, Datum wählen, sofortige Buchungsbestätigung mit Buchungsnummer |
| **Auto mieten** | Fahrzeugkategorie wählen, Mietdauer angeben, automatische Preisberechnung |
| **Taxi bestellen** | Taxi mit Abholort, Zielort, Uhrzeit und Fahrzeugtyp bestellen |
| **Restaurant** | Tisch reservieren mit Datum, Personenzahl, Anlass und Sonderwünschen |
| **Events & Tickets** | Hotel-Events ansehen, Tickets kaufen, einzigartigen Ticket-Code erhalten |

---

## Datenbankschema

**8 Tabellen** in der MySQL-Datenbank `hotel`:

```
users           — Mitarbeiterkonten (Admin / Rezeptionist)
rooms           — Hotelzimmer mit Typ, Preis und Status
guests          — Gästeprofile und Kontaktdaten
reservations    — Zimmerbuchungen mit Check-in/out und Zahlung
services        — Hotelservices-Katalog
service_orders  — Gebuchte Services pro Reservierung
events          — Hotel-Events mit Ticketpreisen
ticket_orders   — Ticket-Bestellungen der Gäste
```

---


## Standard-Anmeldedaten

| Rolle | Benutzername | Passwort |
|-------|-------------|----------|
| Admin | `admin` | `admin123` |
| Rezeptionist | `receptionist` | `rec123` |


---

## Technologien

| Technologie | Details |
|-------------|---------|
| **Programmiersprache** | C# (.NET 8) |
| **UI-Framework** | Windows Presentation Foundation (WPF) |
| **Datenbank** | MySQL 8.x |
| **Datenbankzugriff** | MySql.Data (Connector/NET) |
| **Entwicklungsumgebung** | Visual Studio 2022 |
| **Datenbankverwaltung** | phpMyAdmin über XAMPP |

## Projektstruktur

```text
Nest/
├── Data/          Datenbankzugriff und Repositories
├── Models/        Datenmodelle
├── ViewModels/    Anwendungslogik (MVVM)
├── Views/         WPF-Oberflächen
├── Services/      Querschnittsdienste, z. B. PDF-Erzeugung
└── Assets/        Bilder und weitere statische Ressourcen
```


