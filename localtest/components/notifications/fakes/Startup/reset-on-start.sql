-- Localtest: empty the component's data when it starts. Run by LocaltestDatabaseStartup as
-- platform_notifications_admin after prepare.sql, before the processing loops and the HTTP server start, only when
-- Localtest:Notifications:ResetOnStart is true (the default is false: orders, notifications and the capture persist
-- across restarts, like Storage's data; `studioctl env reset` removes the volume).
-- Not touched: notifications.sendingtimepolicy (lookup values from the migrations) and Yuniql's version table.
-- notifications.resourcelimitlog keeps its rows (migration v0.13 seeds one that claim_email reads); only the e-mail
-- rate limit timeout is cleared.
TRUNCATE notifications.orderschain, notifications.orders, notifications.emailnotifications,
         notifications.smsnotifications, notifications.emailtexts, notifications.smstexts,
         notifications.notificationlog, notifications.statusfeed,
         notifications.deaddeliveryreports, localtest.capture, localtest.inbox RESTART IDENTITY CASCADE;
UPDATE notifications.resourcelimitlog SET emaillimittimeout = NULL;
