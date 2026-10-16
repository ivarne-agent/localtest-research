CREATE TABLE "${SCHEMA}".job_details (
    sched_name text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    description text NULL,
    job_class_name text NOT NULL,
    is_durable bool NOT NULL,
    is_nonconcurrent bool NOT NULL,
    is_update_data bool NOT NULL,
    requests_recovery bool NOT NULL,
    job_data bytea NULL,
    PRIMARY KEY (sched_name, job_name, job_group)
);

CREATE TABLE "${SCHEMA}".triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    description text NULL,
    next_fire_time bigint NULL,
    prev_fire_time bigint NULL,
    priority integer NULL,
    trigger_state text NOT NULL,
    trigger_type text NOT NULL,
    start_time bigint NOT NULL,
    end_time bigint NULL,
    calendar_name text NULL,
    misfire_instr smallint NULL,
    job_data bytea NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, job_name, job_group) REFERENCES "${SCHEMA}".job_details (sched_name, job_name, job_group)
);

CREATE TABLE "${SCHEMA}".simple_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    repeat_count bigint NOT NULL,
    repeat_interval bigint NOT NULL,
    times_triggered bigint NOT NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES "${SCHEMA}".triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);

CREATE TABLE "${SCHEMA}".simprop_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    str_prop_1 text NULL,
    str_prop_2 text NULL,
    str_prop_3 text NULL,
    int_prop_1 integer NULL,
    int_prop_2 integer NULL,
    long_prop_1 bigint NULL,
    long_prop_2 bigint NULL,
    dec_prop_1 numeric NULL,
    dec_prop_2 numeric NULL,
    bool_prop_1 bool NULL,
    bool_prop_2 bool NULL,
    time_zone_id text NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES "${SCHEMA}".triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);

CREATE TABLE "${SCHEMA}".cron_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    cron_expression text NOT NULL,
    time_zone_id text,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES "${SCHEMA}".triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);

CREATE TABLE "${SCHEMA}".blob_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    blob_data bytea NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES "${SCHEMA}".triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);

CREATE TABLE "${SCHEMA}".calendars (
    sched_name text NOT NULL,
    calendar_name text NOT NULL,
    calendar bytea NOT NULL,
    PRIMARY KEY (sched_name, calendar_name)
);

CREATE TABLE "${SCHEMA}".paused_trigger_grps (
    sched_name text NOT NULL,
    trigger_group text NOT NULL,
    PRIMARY KEY (sched_name, trigger_group)
);

CREATE TABLE "${SCHEMA}".fired_triggers (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    instance_name text NOT NULL,
    fired_time bigint NOT NULL,
    sched_time bigint NOT NULL,
    priority integer NOT NULL,
    state text NOT NULL,
    job_name text NULL,
    job_group text NULL,
    is_nonconcurrent bool NOT NULL,
    requests_recovery bool NULL,
    PRIMARY KEY (sched_name, entry_id)
);

CREATE TABLE "${SCHEMA}".scheduler_state (
    sched_name text NOT NULL,
    instance_name text NOT NULL,
    last_checkin_time bigint NOT NULL,
    checkin_interval bigint NOT NULL,
    PRIMARY KEY (sched_name, instance_name)
);

CREATE TABLE "${SCHEMA}".locks (
    sched_name text NOT NULL,
    lock_name text NOT NULL,
    PRIMARY KEY (sched_name, lock_name)
);

CREATE INDEX idx_qrtz_j_req_recovery ON "${SCHEMA}".job_details (requests_recovery);

CREATE INDEX idx_qrtz_t_next_fire_time ON "${SCHEMA}".triggers (next_fire_time);

CREATE INDEX idx_qrtz_t_state ON "${SCHEMA}".triggers (trigger_state);

CREATE INDEX idx_qrtz_t_nft_st ON "${SCHEMA}".triggers (next_fire_time, trigger_state);

CREATE INDEX idx_qrtz_ft_trig_name ON "${SCHEMA}".fired_triggers (trigger_name);

CREATE INDEX idx_qrtz_ft_trig_group ON "${SCHEMA}".fired_triggers (trigger_group);

CREATE INDEX idx_qrtz_ft_trig_nm_gp ON "${SCHEMA}".fired_triggers (sched_name, trigger_name, trigger_group);

CREATE INDEX idx_qrtz_ft_trig_inst_name ON "${SCHEMA}".fired_triggers (instance_name);

CREATE INDEX idx_qrtz_ft_job_name ON "${SCHEMA}".fired_triggers (job_name);

CREATE INDEX idx_qrtz_ft_job_group ON "${SCHEMA}".fired_triggers (job_group);

CREATE INDEX idx_qrtz_ft_job_req_recovery ON "${SCHEMA}".fired_triggers (requests_recovery);
