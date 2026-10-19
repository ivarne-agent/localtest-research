-- Localtest: every start of the events component begins empty, as production data would never survive into a new
-- test session. Run by LocaltestDatabaseStartup as platform_events_admin after prepare.sql, before the claim loop and
-- the HTTP server start, when Localtest:Events:ResetOnStart is true (the default).
-- Not touched: events.event_status (lookup values from the migrations) and Yuniql's version table.
TRUNCATE events.events, events.trace_log, events.subscription, localtest.delivery RESTART IDENTITY;
