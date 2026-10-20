-- Localtest additions to notificationsdb, run by LocaltestDatabaseStartup as platform_notifications_admin after the
-- production migrations, on every start. Idempotent. The production schema (notifications) is not touched.

CREATE SCHEMA IF NOT EXISTS localtest;

-- One row per e-mail address or phone number a message was ever sent to (the exact string, not normalised). The
-- receiving flag is always true in this version; it is the switch a later version turns off per inbox.
CREATE TABLE IF NOT EXISTS localtest.inbox (
    address text PRIMARY KEY,
    channel text NOT NULL,
    receiving boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Every message the publishers fakes were handed (what production would have put on the Service Bus queue), and the
-- instant orders' posts to the Email and SMS hosts' endpoints. The outcome is not stored here: it is the production
-- notification's own result, joined in on read.
CREATE TABLE IF NOT EXISTS localtest.capture (
    id bigserial PRIMARY KEY,
    captured_at timestamptz NOT NULL DEFAULT now(),
    channel text NOT NULL,
    kind text NOT NULL,
    notification_id uuid NOT NULL,
    inbox text NOT NULL,
    recipient text NOT NULL,
    sender text,
    subject text,
    body text NOT NULL,
    content_type text,
    attachments jsonb NOT NULL DEFAULT '[]'
);
CREATE INDEX IF NOT EXISTS capture_notification_id_idx ON localtest.capture (notification_id);
CREATE INDEX IF NOT EXISTS capture_inbox_idx ON localtest.capture (inbox, id);

GRANT USAGE ON SCHEMA localtest TO platform_notifications;
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON localtest.inbox, localtest.capture TO platform_notifications;
GRANT USAGE ON SEQUENCE localtest.capture_id_seq TO platform_notifications;
