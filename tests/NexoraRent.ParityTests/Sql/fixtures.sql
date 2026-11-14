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
