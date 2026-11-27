-- Test data. Ids are fixed so tests can refer to them (see TestData.cs).

INSERT INTO users (id, email, password_hash, first_name, last_name, phone, role) VALUES
  ('11111111-1111-4111-8111-111111111111', 'customer@example.test', 'x', 'Arta', 'Krasniqi', '+38344000000', 'customer'),
  ('22222222-2222-4222-8222-222222222222', 'owner@example.test', 'x', 'Blerim', 'Hoxha', NULL, 'owner'),
  ('33333333-3333-4333-8333-333333333333', 'other@example.test', 'x', 'Dita', 'Berisha', NULL, 'customer');

INSERT INTO cars (id, brand, model, year, slug, category, fuel, transmission, license_plate, price_per_day, price_per_week, deposit, features, is_available, is_featured, sort_order, created_at) VALUES
  ('aaaaaaaa-0000-4000-8000-000000000001', 'Volkswagen', 'Golf 8', 2023, 'volkswagen-golf-8', 'compact', 'diesel', 'manual', '01-111-AA', 35.00, 210.00, 200.00, '["Bluetooth","Air conditioning"]', true, true, 1, '2026-01-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000002', 'BMW', 'X5', 2024, 'bmw-x5', 'suv', 'hybrid', 'automatic', '01-222-BB', 95.50, NULL, 500.00, '["Leather seats"]', true, true, 2, '2026-02-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000003', 'Skoda', 'Octavia', 2022, 'skoda-octavia', 'sedan', 'diesel', 'automatic', '01-333-CC', 45.00, NULL, 250.00, NULL, true, false, 3, '2026-03-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000004', 'Fiat', 'Panda', 2020, 'fiat-panda', 'economy', 'petrol', 'manual', '01-444-DD', 20.00, NULL, 100.00, '[]', false, false, 4, '2026-04-01 08:00:00');

INSERT INTO car_images (id, car_id, url, alt_text, is_primary, sort_order) VALUES
  ('bbbbbbbb-0000-4000-8000-000000000001', 'aaaaaaaa-0000-4000-8000-000000000001', 'https://img.example.test/golf-side.jpg', 'Golf side', false, 2),
  ('bbbbbbbb-0000-4000-8000-000000000002', 'aaaaaaaa-0000-4000-8000-000000000001', 'https://img.example.test/golf-front.jpg', 'Golf front', true, 1);

INSERT INTO locations (id, name, address, city, lat, lng, is_active) VALUES
  ('eeeeeeee-0000-4000-8000-000000000001', 'Drenas Center', 'Main street', 'Drenas', 42.6271000, 20.8900000, true),
  ('eeeeeeee-0000-4000-8000-000000000002', 'Pristina Airport', NULL, 'Pristina', NULL, NULL, true),
  ('eeeeeeee-0000-4000-8000-000000000003', 'Closed Office', NULL, 'Peja', NULL, NULL, false);

INSERT INTO extras (id, name, description, price_per_day, icon, is_active) VALUES
  ('ffffffff-0000-4000-8000-000000000001', 'GPS Navigation', 'Portable GPS', 5.00, 'navigation', true),
  ('ffffffff-0000-4000-8000-000000000002', 'Child Seat', NULL, 3.00, 'baby', true),
  ('ffffffff-0000-4000-8000-000000000003', 'Retired Extra', NULL, 9.00, NULL, false);

-- 20% more for the BMW in July 2031.
INSERT INTO seasonal_pricing (id, car_id, name, start_date, end_date, multiplier) VALUES
  ('99999999-0000-4000-8000-000000000001', 'aaaaaaaa-0000-4000-8000-000000000002', 'Summer', '2031-07-01', '2031-07-31', 1.20);

INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price) VALUES
  ('cccccccc-0000-4000-8000-000000000001', 'DRC-1', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-12-10 10:00:00', '2026-12-15 10:00:00', 'confirmed', 5, 35.00, 175.00),
  ('cccccccc-0000-4000-8000-000000000002', 'DRC-2', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-11-01 10:00:00', '2026-11-03 10:00:00', 'completed', 2, 35.00, 70.00),
  ('cccccccc-0000-4000-8000-000000000003', 'DRC-3', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-12-20 10:00:00', '2026-12-22 10:00:00', 'cancelled', 2, 35.00, 70.00),
  -- Completed and not reviewed yet. On the Skoda, so the Golf ratings and bookings stay the same.
  ('cccccccc-0000-4000-8000-000000000004', 'DRC-4', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000003', '2025-05-01 10:00:00', '2025-05-03 10:00:00', 'completed', 2, 45.00, 90.00),
  -- Belongs to another customer.
  ('cccccccc-0000-4000-8000-000000000005', 'DRC-5', '33333333-3333-4333-8333-333333333333', 'aaaaaaaa-0000-4000-8000-000000000003', '2025-06-01 10:00:00', '2025-06-03 10:00:00', 'completed', 2, 45.00, 90.00);

INSERT INTO reviews (id, user_id, reservation_id, rating, comment, is_approved, created_at) VALUES
  ('dddddddd-0000-4000-8000-000000000001', '11111111-1111-4111-8111-111111111111', 'cccccccc-0000-4000-8000-000000000002', 4, 'Clean and economical', true, '2026-11-04 09:00:00'),
  ('dddddddd-0000-4000-8000-000000000002', '11111111-1111-4111-8111-111111111111', 'cccccccc-0000-4000-8000-000000000003', 1, 'Not approved yet', false, '2026-11-05 09:00:00');

-- Auth tests (AuthSessionApiTests.cs). The hash was made by bcryptjs 2.4.3 in Express for
-- 'correct horse battery staple', so signing in proves the two bcrypt libraries agree.
INSERT INTO users (id, email, password_hash, first_name, last_name, phone, role) VALUES
  ('44444444-4444-4444-8444-444444444444', 'legacy@example.test', '$2a$12$PSPRUFzAiMBKknv4s/E7oO6mniEytDIPCmM/hxiNHQt0sdvCdANLC', 'Lira', 'Gashi', NULL, 'customer');

INSERT INTO loyalty_accounts (id, user_id, points_balance, lifetime_points, tier) VALUES
  ('bbbbbbbb-0000-4000-8000-000000000001', '22222222-2222-4222-8222-222222222222', 340, 900, 'gold');
