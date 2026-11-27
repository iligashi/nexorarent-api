-- Subset of the nexorarent schema (server/src/db/migrations in the Express repo)
-- covering the tables this API reads. Keep in sync when those tables change.

CREATE TABLE users (
  id CHAR(36) PRIMARY KEY,
  email VARCHAR(255) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  first_name VARCHAR(100) NOT NULL,
  last_name VARCHAR(100) NOT NULL,
  phone VARCHAR(30),
  role ENUM('customer','staff','manager','owner','driver') DEFAULT 'customer',
  avatar_url TEXT,
  is_verified BOOLEAN DEFAULT false,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE cars (
  id CHAR(36) PRIMARY KEY,
  brand VARCHAR(100) NOT NULL,
  model VARCHAR(100) NOT NULL,
  year INT NOT NULL,
  slug VARCHAR(255) UNIQUE NOT NULL,
  category ENUM('economy','compact','sedan','suv','luxury','van','sports') NOT NULL,
  fuel ENUM('petrol','diesel','hybrid','electric') NOT NULL,
  transmission ENUM('manual','automatic') NOT NULL,
  seats INT DEFAULT 5,
  doors INT DEFAULT 4,
  horsepower INT,
  engine_cc INT,
  color VARCHAR(50),
  license_plate VARCHAR(20) UNIQUE,
  vin VARCHAR(17) UNIQUE,
  mileage INT DEFAULT 0,
  price_per_day DECIMAL(10,2) NOT NULL,
  price_per_week DECIMAL(10,2),
  deposit DECIMAL(10,2) DEFAULT 0,
  description TEXT,
  features JSON,
  is_available BOOLEAN DEFAULT true,
  is_featured BOOLEAN DEFAULT false,
  sort_order INT DEFAULT 0,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE car_images (
  id CHAR(36) PRIMARY KEY,
  car_id CHAR(36) NOT NULL,
  url TEXT NOT NULL,
  alt_text VARCHAR(255),
  is_primary BOOLEAN DEFAULT false,
  sort_order INT DEFAULT 0,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (car_id) REFERENCES cars(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE locations (
  id CHAR(36) PRIMARY KEY,
  name VARCHAR(200) NOT NULL,
  address TEXT,
  city VARCHAR(100),
  lat DECIMAL(10,7),
  lng DECIMAL(10,7),
  is_active BOOLEAN DEFAULT true
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE extras (
  id CHAR(36) PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  description TEXT,
  price_per_day DECIMAL(10,2) NOT NULL,
  icon VARCHAR(50),
  is_active BOOLEAN DEFAULT true
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE reservations (
  id CHAR(36) PRIMARY KEY,
  reservation_no VARCHAR(20) UNIQUE NOT NULL,
  user_id CHAR(36),
  car_id CHAR(36) NOT NULL,
  pickup_location CHAR(36),
  dropoff_location CHAR(36),
  pickup_date DATETIME NOT NULL,
  dropoff_date DATETIME NOT NULL,
  status ENUM('pending','confirmed','active','completed','cancelled','rejected') DEFAULT 'pending',
  total_days INT NOT NULL,
  daily_rate DECIMAL(10,2) NOT NULL,
  extras_total DECIMAL(10,2) DEFAULT 0,
  discount DECIMAL(10,2) DEFAULT 0,
  total_price DECIMAL(10,2) NOT NULL,
  guest_name VARCHAR(200),
  guest_email VARCHAR(255),
  guest_phone VARCHAR(30),
  notes TEXT,
  admin_notes TEXT,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  loyalty_points_earned INT DEFAULT 0,
  loyalty_points_redeemed INT DEFAULT 0,
  FOREIGN KEY (user_id) REFERENCES users(id),
  FOREIGN KEY (car_id) REFERENCES cars(id),
  FOREIGN KEY (pickup_location) REFERENCES locations(id),
  FOREIGN KEY (dropoff_location) REFERENCES locations(id),
  INDEX idx_res_car_status_dates (car_id, status, pickup_date, dropoff_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE reservation_extras (
  id CHAR(36) PRIMARY KEY,
  reservation_id CHAR(36) NOT NULL,
  extra_id CHAR(36) NOT NULL,
  quantity INT DEFAULT 1,
  price DECIMAL(10,2) NOT NULL,
  FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE CASCADE,
  FOREIGN KEY (extra_id) REFERENCES extras(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE seasonal_pricing (
  id CHAR(36) PRIMARY KEY,
  car_id CHAR(36),
  name VARCHAR(100),
  start_date DATE NOT NULL,
  end_date DATE NOT NULL,
  multiplier DECIMAL(4,2) DEFAULT 1.00,
  FOREIGN KEY (car_id) REFERENCES cars(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE pricing_rules (
  id CHAR(36) PRIMARY KEY,
  name VARCHAR(200) NOT NULL,
  type ENUM('demand','advance_booking','duration','last_minute','event') NOT NULL,
  car_id CHAR(36),
  category VARCHAR(50),
  multiplier DECIMAL(4,2) NOT NULL DEFAULT 1.00,
  conditions JSON NOT NULL,
  priority INT DEFAULT 0,
  is_active BOOLEAN DEFAULT true,
  start_date DATE,
  end_date DATE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_pricing_active (is_active, start_date, end_date),
  FOREIGN KEY (car_id) REFERENCES cars(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- From the Express migrations 002_features.sql and 005_booking_and_session_safety.sql.
CREATE TABLE loyalty_accounts (
  id CHAR(36) PRIMARY KEY,
  user_id CHAR(36) UNIQUE NOT NULL,
  points_balance INT DEFAULT 0,
  lifetime_points INT DEFAULT 0,
  tier ENUM('bronze','silver','gold','platinum') DEFAULT 'bronze',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE loyalty_transactions (
  id CHAR(36) PRIMARY KEY,
  account_id CHAR(36) NOT NULL,
  reservation_id CHAR(36),
  type ENUM('earn','redeem','bonus','expire','adjust') NOT NULL,
  points INT NOT NULL,
  description VARCHAR(255),
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_loyalty_tx_account (account_id),
  FOREIGN KEY (account_id) REFERENCES loyalty_accounts(id) ON DELETE CASCADE,
  FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE loyalty_rewards (
  id CHAR(36) PRIMARY KEY,
  name VARCHAR(200) NOT NULL,
  description TEXT,
  type ENUM('discount_percent','discount_fixed','free_day','upgrade','free_extra') NOT NULL,
  value DECIMAL(10,2) NOT NULL,
  points_cost INT NOT NULL,
  is_active BOOLEAN DEFAULT true,
  min_tier ENUM('bronze','silver','gold','platinum') DEFAULT 'bronze'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE notification_templates (
  id CHAR(36) PRIMARY KEY,
  type ENUM('booking_confirmation','pickup_reminder','return_reminder','review_request','status_change','loyalty_points') NOT NULL,
  channel ENUM('whatsapp','email','sms') NOT NULL DEFAULT 'whatsapp',
  template_name VARCHAR(200) NOT NULL,
  template_body TEXT NOT NULL,
  is_active BOOLEAN DEFAULT true,
  UNIQUE KEY uq_template_type_channel (type, channel)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE notification_log (
  id CHAR(36) PRIMARY KEY,
  reservation_id CHAR(36),
  user_id CHAR(36),
  type VARCHAR(50) NOT NULL,
  channel VARCHAR(20) NOT NULL,
  recipient VARCHAR(255) NOT NULL,
  status ENUM('pending','sent','delivered','failed','read') DEFAULT 'pending',
  error_message TEXT,
  sent_at TIMESTAMP NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_notif_status (status),
  INDEX idx_notif_reservation (reservation_id),
  FOREIGN KEY (reservation_id) REFERENCES reservations(id) ON DELETE SET NULL,
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE car_expenses (
  id CHAR(36) PRIMARY KEY,
  car_id CHAR(36) NOT NULL,
  category ENUM('insurance','repair','fuel','tire','wash','parking','tax','registration','other') NOT NULL,
  description TEXT,
  amount DECIMAL(10,2) NOT NULL,
  expense_date DATE NOT NULL,
  vendor VARCHAR(200),
  created_by CHAR(36),
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_expense_car (car_id),
  INDEX idx_expense_date (expense_date),
  INDEX idx_expense_category (category),
  FOREIGN KEY (car_id) REFERENCES cars(id) ON DELETE CASCADE,
  FOREIGN KEY (created_by) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE reviews (
  id CHAR(36) PRIMARY KEY,
  user_id CHAR(36) NOT NULL,
  reservation_id CHAR(36) UNIQUE,
  rating INT CHECK (rating BETWEEN 1 AND 5),
  comment TEXT,
  is_approved BOOLEAN DEFAULT false,
  admin_reply TEXT,
  replied_at TIMESTAMP NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (user_id) REFERENCES users(id),
  FOREIGN KEY (reservation_id) REFERENCES reservations(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE refresh_token_sessions (
  id CHAR(36) PRIMARY KEY,
  user_id CHAR(36) NOT NULL,
  token_hash CHAR(64) NOT NULL UNIQUE,
  expires_at DATETIME NOT NULL,
  revoked_at DATETIME NULL,
  replaced_by_session_id CHAR(36) NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_refresh_sessions_user_revoked (user_id, revoked_at),
  INDEX idx_refresh_sessions_expiry (expires_at),
  CONSTRAINT fk_refresh_sessions_user
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- From the Express migrations 004, 005 and 006.
CREATE TABLE admin_audit_log (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  request_id CHAR(36) NOT NULL,
  user_id CHAR(36) NULL,
  method VARCHAR(10) NOT NULL,
  path VARCHAR(500) NOT NULL,
  status_code SMALLINT UNSIGNED NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  action VARCHAR(120) NOT NULL DEFAULT 'admin_action',
  target_id VARCHAR(100) NULL,
  change_summary JSON NULL,
  INDEX idx_admin_audit_user_created (user_id, created_at),
  INDEX idx_admin_audit_created (created_at),
  CONSTRAINT fk_admin_audit_user
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
