CREATE COLLATION case_insensitive (
    provider = icu,
    -- Use 'und' (undefined) locale with 'ks-level2' for case insensitivity
    locale = 'und-u-ks-level2',
    deterministic = false);
