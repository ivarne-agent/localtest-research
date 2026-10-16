WITH deleted_parties_users_with_usernames AS (
    SELECT p."uuid"
    FROM register.party p
    INNER JOIN register.user u USING ("uuid")
    WHERE p.is_deleted AND u.username IS NOT NULL
)
UPDATE register.user u
SET username = NULL
FROM deleted_parties_users_with_usernames dpu
WHERE u."uuid" = dpu."uuid";
