-- Extra rows for the parity run, loaded after the Express migrations and seed.
-- The Express seed has no blog posts, so add published and unpublished ones
-- with distinct dates (the blog list is ordered by published_at).

INSERT INTO blog_posts (id, title, slug, content, excerpt, cover_image, author_id, is_published, published_at)
SELECT 'eeeeeeee-0000-4000-8000-000000000001', 'Parity first post', 'parity-first-post', '<p>First</p>', 'First excerpt', NULL, id, true, '2026-01-10 09:00:00'
FROM users WHERE role = 'owner' LIMIT 1;

INSERT INTO blog_posts (id, title, slug, content, excerpt, cover_image, author_id, is_published, published_at) VALUES
  ('eeeeeeee-0000-4000-8000-000000000002', 'Parity second post', 'parity-second-post', '<p>Second</p>', NULL, 'https://img.example.test/cover.jpg', NULL, true, '2026-02-10 09:00:00'),
  ('eeeeeeee-0000-4000-8000-000000000003', 'Parity draft', 'parity-draft', '<p>Draft</p>', NULL, NULL, NULL, false, NULL);

-- One booking so availability returns blocked dates.
INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price)
SELECT 'ffffffff-0000-4000-8000-000000000001', 'DRC-PARITY-1', NULL, id, '2030-06-03 10:00:00', '2030-06-07 10:00:00', 'confirmed', 4, price_per_day, price_per_day * 4
FROM cars ORDER BY slug LIMIT 1;

-- Write parity tests (WriteParityTests.cs). The password hash is not a real hash:
-- nobody can sign in as these users; the tests sign their own access tokens.
INSERT INTO users (id, email, password_hash, first_name, last_name, phone, role) VALUES
  ('dddddddd-0000-4000-8000-000000000001', 'parity-customer@example.test', 'not-a-hash', 'Parity', 'Customer', '+38344000001', 'customer'),
  ('dddddddd-0000-4000-8000-000000000002', 'parity-other@example.test', 'not-a-hash', 'Parity', 'Other', NULL, 'customer'),
  ('dddddddd-0000-4000-8000-000000000003', 'parity-staff@example.test', 'not-a-hash', 'Parity', 'Staff', NULL, 'staff');

-- A car that cannot be booked, an inactive location and an inactive extra.
INSERT INTO cars (id, brand, model, year, slug, category, fuel, transmission, price_per_day, is_available)
VALUES ('dddddddd-0000-4000-8000-0000000000a1', 'Parity', 'Retired', 2020, 'parity-retired', 'economy', 'petrol', 'manual', 20.00, false);
INSERT INTO locations (id, name, city, is_active) VALUES ('dddddddd-0000-4000-8000-0000000000b1', 'Parity Closed Office', 'Peja', false);
INSERT INTO extras (id, name, price_per_day, is_active) VALUES ('dddddddd-0000-4000-8000-0000000000c1', 'Parity Retired Extra', 9.00, false);

-- 15% more for the second car (by slug) in July 2032, to compare seasonal prices.
INSERT INTO seasonal_pricing (id, car_id, name, start_date, end_date, multiplier)
SELECT 'dddddddd-0000-4000-8000-0000000000d1', id, 'Parity summer', '2032-07-01', '2032-07-31', 1.15
FROM cars WHERE is_available = true ORDER BY slug LIMIT 1 OFFSET 1;

-- The customer's bookings: confirmed with an extra, pending (to cancel), completed (to review).
-- And one completed booking of another customer.
INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_location, dropoff_location, pickup_date, dropoff_date, status, total_days, daily_rate, extras_total, total_price, notes)
SELECT 'dddddddd-0000-4000-8000-0000000000e1', 'DRC-PARITY-E1', 'dddddddd-0000-4000-8000-000000000001', c.id, l.id, l.id, '2033-01-10 10:00:00', '2033-01-12 10:00:00', 'confirmed', 2, c.price_per_day, 10.00, c.price_per_day * 2 + 10.00, 'Window seat'
FROM cars c, locations l WHERE c.is_available = true AND l.is_active = true ORDER BY c.slug, l.name LIMIT 1;

INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price)
SELECT 'dddddddd-0000-4000-8000-0000000000e2', 'DRC-PARITY-E2', 'dddddddd-0000-4000-8000-000000000001', id, '2033-02-10 10:00:00', '2033-02-11 10:00:00', 'pending', 1, price_per_day, price_per_day
FROM cars WHERE is_available = true ORDER BY slug LIMIT 1;

INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price)
SELECT 'dddddddd-0000-4000-8000-0000000000e3', 'DRC-PARITY-E3', 'dddddddd-0000-4000-8000-000000000001', id, '2025-03-10 10:00:00', '2025-03-12 10:00:00', 'completed', 2, price_per_day, price_per_day * 2
FROM cars WHERE is_available = true ORDER BY slug LIMIT 1;

INSERT INTO reservations (id, reservation_no, user_id, car_id, pickup_date, dropoff_date, status, total_days, daily_rate, total_price)
SELECT 'dddddddd-0000-4000-8000-0000000000e4', 'DRC-PARITY-E4', 'dddddddd-0000-4000-8000-000000000002', id, '2025-04-10 10:00:00', '2025-04-12 10:00:00', 'completed', 2, price_per_day, price_per_day * 2
FROM cars WHERE is_available = true ORDER BY slug LIMIT 1;

INSERT INTO reservation_extras (id, reservation_id, extra_id, quantity, price)
SELECT 'dddddddd-0000-4000-8000-0000000000f1', 'dddddddd-0000-4000-8000-0000000000e1', id, 1, 10.00
FROM extras WHERE is_active = true ORDER BY name LIMIT 1;
