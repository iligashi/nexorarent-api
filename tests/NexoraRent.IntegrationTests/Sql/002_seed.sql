-- Test data. Ids are fixed so tests can refer to them (see TestData.cs).

INSERT INTO users (id, email, password_hash, first_name, last_name, phone, role) VALUES
  ('11111111-1111-4111-8111-111111111111', 'customer@example.test', 'x', 'Arta', 'Krasniqi', '+38344000000', 'customer'),
  ('22222222-2222-4222-8222-222222222222', 'owner@example.test', 'x', 'Blerim', 'Hoxha', NULL, 'owner');

INSERT INTO cars (id, brand, model, year, slug, category, fuel, transmission, license_plate, price_per_day, price_per_week, deposit, features, is_available, is_featured, sort_order, created_at) VALUES
  ('aaaaaaaa-0000-4000-8000-000000000001', 'Volkswagen', 'Golf 8', 2023, 'volkswagen-golf-8', 'compact', 'diesel', 'manual', '01-111-AA', 35.00, 210.00, 200.00, '["Bluetooth","Air conditioning"]', true, true, 1, '2026-01-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000002', 'BMW', 'X5', 2024, 'bmw-x5', 'suv', 'hybrid', 'automatic', '01-222-BB', 95.50, NULL, 500.00, '["Leather seats"]', true, true, 2, '2026-02-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000003', 'Skoda', 'Octavia', 2022, 'skoda-octavia', 'sedan', 'diesel', 'automatic', '01-333-CC', 45.00, NULL, 250.00, NULL, true, false, 3, '2026-03-01 08:00:00'),
  ('aaaaaaaa-0000-4000-8000-000000000004', 'Fiat', 'Panda', 2020, 'fiat-panda', 'economy', 'petrol', 'manual', '01-444-DD', 20.00, NULL, 100.00, '[]', false, false, 4, '2026-04-01 08:00:00');

INSERT INTO car_images (id, car_id, url, alt_text, is_primary, sort_order) VALUES
  ('bbbbbbbb-0000-4000-8000-000000000001', 'aaaaaaaa-0000-4000-8000-000000000001', 'https://img.example.test/golf-side.jpg', 'Golf side', false, 2),
  ('bbbbbbbb-0000-4000-8000-000000000002', 'aaaaaaaa-0000-4000-8000-000000000001', 'https://img.example.test/golf-front.jpg', 'Golf front', true, 1);

INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price) VALUES
  ('cccccccc-0000-4000-8000-000000000001', 'DRC-1', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-12-10 10:00:00', '2026-12-15 10:00:00', 'confirmed', 5, 35.00, 175.00),
  ('cccccccc-0000-4000-8000-000000000002', 'DRC-2', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-11-01 10:00:00', '2026-11-03 10:00:00', 'completed', 2, 35.00, 70.00),
  ('cccccccc-0000-4000-8000-000000000003', 'DRC-3', '11111111-1111-4111-8111-111111111111', 'aaaaaaaa-0000-4000-8000-000000000001', '2026-12-20 10:00:00', '2026-12-22 10:00:00', 'cancelled', 2, 35.00, 70.00);

INSERT INTO reviews (id, user_id, reservation_id, rating, comment, is_approved, created_at) VALUES
  ('dddddddd-0000-4000-8000-000000000001', '11111111-1111-4111-8111-111111111111', 'cccccccc-0000-4000-8000-000000000002', 4, 'Clean and economical', true, '2026-11-04 09:00:00'),
  ('dddddddd-0000-4000-8000-000000000002', '11111111-1111-4111-8111-111111111111', 'cccccccc-0000-4000-8000-000000000003', 1, 'Not approved yet', false, '2026-11-05 09:00:00');
