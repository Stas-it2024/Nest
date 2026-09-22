-- ============================================================
-- Hotel Management System - MySQL Database
-- Run this in phpMyAdmin or MySQL Workbench
-- ============================================================

CREATE DATABASE IF NOT EXISTS hotel;
USE hotel;

-- -------------------------------------------------------
-- USERS (Admin / Receptionist roles)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    username    VARCHAR(50)  NOT NULL UNIQUE,
    password    VARCHAR(255) NOT NULL,
    full_name   VARCHAR(100) NOT NULL,
    role        ENUM('Admin','Receptionist') NOT NULL DEFAULT 'Receptionist',
    status      ENUM('Active','Inactive')    NOT NULL DEFAULT 'Active',
    date_added  DATETIME     DEFAULT CURRENT_TIMESTAMP,
    date_delete DATETIME     DEFAULT NULL
);

-- Default admin account  (password: admin123)
INSERT INTO users (username, password, full_name, role, status)
VALUES ('admin', 'admin123', 'System Administrator', 'Admin', 'Active');

-- Default receptionist  (password: rec123)
INSERT INTO users (username, password, full_name, role, status)
VALUES ('receptionist', 'rec123', 'Front Desk Staff', 'Receptionist', 'Active');

-- -------------------------------------------------------
-- ROOMS
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS rooms (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    room_number VARCHAR(10)  NOT NULL UNIQUE,
    room_type   ENUM('Standard','Deluxe','Suite','Executive') NOT NULL DEFAULT 'Standard',
    capacity    INT          NOT NULL DEFAULT 1,
    price_per_night DECIMAL(10,2) NOT NULL,
    status      ENUM('Available','Occupied','Maintenance','Reserved') NOT NULL DEFAULT 'Available',
    description TEXT,
    image_path  VARCHAR(500) DEFAULT NULL,
    date_added  DATETIME     DEFAULT CURRENT_TIMESTAMP,
    date_delete DATETIME     DEFAULT NULL
);

-- Sample rooms
INSERT INTO rooms (room_number, room_type, capacity, price_per_night, status, description) VALUES
('101', 'Standard',  1, 59.99,  'Available', 'Cozy standard single room with city view'),
('102', 'Standard',  2, 79.99,  'Available', 'Standard twin room, ground floor'),
('201', 'Deluxe',    2, 119.99, 'Available', 'Deluxe double room with balcony'),
('202', 'Deluxe',    2, 129.99, 'Available', 'Deluxe king room, sea view'),
('301', 'Suite',     4, 249.99, 'Available', 'Junior suite with living area and kitchenette'),
('302', 'Suite',     4, 299.99, 'Available', 'Family suite, two bedrooms'),
('401', 'Executive', 2, 399.99, 'Available', 'Executive floor, panoramic view, butler service'),
('402', 'Executive', 2, 449.99, 'Available', 'Presidential suite, full floor access');

-- -------------------------------------------------------
-- GUESTS
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS guests (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    first_name   VARCHAR(50)  NOT NULL,
    last_name    VARCHAR(50)  NOT NULL,
    email        VARCHAR(100) DEFAULT NULL,
    phone        VARCHAR(20)  DEFAULT NULL,
    id_type      ENUM('Passport','National ID','Driver License','Other') DEFAULT 'Passport',
    id_number    VARCHAR(50)  DEFAULT NULL,
    nationality  VARCHAR(50)  DEFAULT NULL,
    date_added   DATETIME     DEFAULT CURRENT_TIMESTAMP
);

-- -------------------------------------------------------
-- RESERVATIONS  (one per stay)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS reservations (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    reservation_code VARCHAR(20) NOT NULL UNIQUE,
    guest_id        INT          NOT NULL,
    room_id         INT          NOT NULL,
    check_in_date   DATE         NOT NULL,
    check_out_date  DATE         NOT NULL,
    nights          INT          NOT NULL DEFAULT 1,
    adults          INT          NOT NULL DEFAULT 1,
    children        INT          NOT NULL DEFAULT 0,
    total_amount    DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    amount_paid     DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    payment_method  ENUM('Cash','Credit Card','Debit Card','Bank Transfer','Online') DEFAULT 'Cash',
    status          ENUM('Reserved','Checked In','Checked Out','Cancelled') NOT NULL DEFAULT 'Reserved',
    special_requests TEXT,
    receptionist_id INT          DEFAULT NULL,
    date_created    DATETIME     DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (guest_id) REFERENCES guests(id),
    FOREIGN KEY (room_id)  REFERENCES rooms(id)
);

-- -------------------------------------------------------
-- SERVICES  (extra charges during stay)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS services (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    service_name VARCHAR(100) NOT NULL,
    category     ENUM('Food & Beverage','Laundry','Spa','Transport','Other') DEFAULT 'Other',
    price        DECIMAL(10,2) NOT NULL,
    status       ENUM('Available','Unavailable') NOT NULL DEFAULT 'Available',
    date_added   DATETIME     DEFAULT CURRENT_TIMESTAMP,
    date_delete  DATETIME     DEFAULT NULL
);

INSERT INTO services (service_name, category, price, status) VALUES
('Breakfast Buffet',  'Food & Beverage', 18.00, 'Available'),
('Room Service Meal', 'Food & Beverage', 25.00, 'Available'),
('Laundry (per kg)',  'Laundry',          8.00, 'Available'),
('Dry Cleaning',      'Laundry',         15.00, 'Available'),
('Spa - Full Body',   'Spa',             80.00, 'Available'),
('Spa - Massage',     'Spa',             60.00, 'Available'),
('Airport Transfer',  'Transport',       40.00, 'Available'),
('City Tour',         'Transport',       55.00, 'Available');

-- -------------------------------------------------------
-- SERVICE ORDERS  (services added to a reservation)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS service_orders (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    reservation_id  INT NOT NULL,
    service_id      INT NOT NULL,
    quantity        INT NOT NULL DEFAULT 1,
    unit_price      DECIMAL(10,2) NOT NULL,
    total_price     DECIMAL(10,2) NOT NULL,
    date_ordered    DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (reservation_id) REFERENCES reservations(id),
    FOREIGN KEY (service_id)     REFERENCES services(id)
);

-- -------------------------------------------------------
-- CAR RENTALS  (Customer Portal rental requests)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS car_rentals (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    reservation_id   INT DEFAULT NULL,
    driver_name      VARCHAR(100) NOT NULL,
    phone            VARCHAR(30) NOT NULL,
    vehicle_category VARCHAR(50) NOT NULL,
    pickup_date      DATE NOT NULL,
    return_date      DATE NOT NULL,
    daily_price      DECIMAL(10,2) NOT NULL,
    total_price      DECIMAL(10,2) NOT NULL,
    status           ENUM('Requested','Confirmed','Completed','Cancelled') NOT NULL DEFAULT 'Requested',
    date_created     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_car_rental_dates CHECK (return_date > pickup_date),
    CONSTRAINT chk_car_rental_daily_price CHECK (daily_price >= 0),
    CONSTRAINT chk_car_rental_total_price CHECK (total_price >= 0),
    CONSTRAINT fk_car_rental_reservation
        FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE SET NULL,
    INDEX idx_car_rental_dates (pickup_date, return_date),
    INDEX idx_car_rental_status (status)
);

-- -------------------------------------------------------
-- TAXI ORDERS  (Customer Portal taxi requests)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS taxi_orders (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    reservation_id  INT DEFAULT NULL,
    guest_name      VARCHAR(100) NOT NULL,
    guest_phone     VARCHAR(30) DEFAULT NULL,
    pickup_location VARCHAR(255) NOT NULL,
    pickup_date     DATE NOT NULL,
    pickup_time     TIME NOT NULL,
    vehicle_type    VARCHAR(50) NOT NULL,
    status          ENUM('Requested','Confirmed','Completed','Cancelled') NOT NULL DEFAULT 'Requested',
    date_ordered    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_taxi_order_reservation
        FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE SET NULL,
    INDEX idx_taxi_pickup (pickup_date, pickup_time),
    INDEX idx_taxi_status (status)
);

-- -------------------------------------------------------
-- RESTAURANT RESERVATIONS  (One reservation per table)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS restaurant_reservations (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    reservation_id   INT DEFAULT NULL,
    guest_name       VARCHAR(100) NOT NULL,
    reservation_date DATE NOT NULL,
    reservation_time TIME NOT NULL,
    guest_count      INT NOT NULL,
    occasion         VARCHAR(50) NOT NULL,
    special_requests TEXT DEFAULT NULL,
    status           ENUM('Requested','Confirmed','Completed','Cancelled') NOT NULL DEFAULT 'Requested',
    date_created     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_restaurant_guest_count CHECK (guest_count BETWEEN 1 AND 8),
    CONSTRAINT fk_restaurant_reservation
        FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE SET NULL,
    INDEX idx_restaurant_datetime (reservation_date, reservation_time),
    INDEX idx_restaurant_status (status)
);

-- -------------------------------------------------------
-- EVENTS  (Hotel events with ticket pricing)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS events (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    title        VARCHAR(200) NOT NULL,
    description  TEXT,
    event_date   DATE NOT NULL,
    event_time   TIME DEFAULT '19:00:00',
    location     VARCHAR(200) DEFAULT 'Hotel Lobby',
    ticket_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    max_tickets  INT DEFAULT 100,
    sold_tickets INT DEFAULT 0,
    image_url    VARCHAR(500) DEFAULT NULL,
    status       ENUM('Aktiv','Inaktiv','Ausgebucht') NOT NULL DEFAULT 'Aktiv',
    created_by   INT DEFAULT NULL,
    date_created DATETIME DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO events (title, description, event_date, event_time, location, ticket_price, max_tickets, status) VALUES
('Willkommens-Abend', 'Taeglich: Begruessung der Gaeste mit Cocktails und Live-Musik im Panorama-Saal.', CURDATE(), '19:00:00', 'Panorama-Saal', 0.00, 200, 'Aktiv'),
('Wine & Dine Abend', 'Exklusives 5-Gang-Menue mit erlesenen Weinen aus aller Welt.', DATE_ADD(CURDATE(), INTERVAL 1 DAY), '19:30:00', 'Restaurant Grand', 85.00, 40, 'Aktiv'),
('Yoga am Morgen', 'Entspannende Yoga-Session am Pool fuer alle Gaeste.', CURDATE(), '07:30:00', 'Poolterrasse', 15.00, 20, 'Aktiv'),
('Live Jazz Night', 'Jazztrio spielt klassischen Jazz und Soul. Getraenke inklusive.', DATE_ADD(CURDATE(), INTERVAL 2 DAY), '20:00:00', 'Hotelbar', 25.00, 60, 'Aktiv');

-- -------------------------------------------------------
-- TICKET ORDERS  (Guest ticket purchases)
-- -------------------------------------------------------
CREATE TABLE IF NOT EXISTS ticket_orders (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    event_id        INT NOT NULL,
    guest_name      VARCHAR(100) NOT NULL,
    guest_email     VARCHAR(100),
    guest_phone     VARCHAR(30),
    quantity        INT NOT NULL DEFAULT 1,
    unit_price      DECIMAL(10,2) NOT NULL,
    total_price     DECIMAL(10,2) NOT NULL,
    ticket_code     VARCHAR(30) NOT NULL UNIQUE,
    reservation_id  INT DEFAULT NULL,
    date_ordered    DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (event_id) REFERENCES events(id)
);
