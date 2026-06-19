-- One append-only table holds every event. Aggregation happens at read time
-- with GROUP BY queries, so we never have to migrate when we add a new event.
CREATE TABLE IF NOT EXISTS events (
  id           bigserial PRIMARY KEY,
  received_at  timestamptz NOT NULL DEFAULT now(), -- server time (trustworthy)
  event_time   timestamptz,                        -- client-reported time (may be skewed)
  device_id    text,                               -- anonymous per-device id (unique players)
  session_id   text,                               -- one play session (one page load)
  name         text NOT NULL,                      -- e.g. 'death', 'level_start'
  props        jsonb NOT NULL DEFAULT '{}'::jsonb,  -- arbitrary event fields
  app_version  text,
  origin       text                                -- where the game was served from
);

CREATE INDEX IF NOT EXISTS idx_events_name    ON events (name);
CREATE INDEX IF NOT EXISTS idx_events_session ON events (session_id);
CREATE INDEX IF NOT EXISTS idx_events_device  ON events (device_id);
CREATE INDEX IF NOT EXISTS idx_events_time    ON events (received_at);
