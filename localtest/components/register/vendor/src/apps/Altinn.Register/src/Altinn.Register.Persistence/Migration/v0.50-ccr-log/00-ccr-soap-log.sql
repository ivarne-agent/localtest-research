CREATE TABLE register.ccr_soap_log (
    id uuid NOT NULL PRIMARY KEY,
    request_start timestamptz NOT NULL,
    request_url text NOT NULL,
    request_headers jsonb NOT NULL,
    request_body text NOT NULL,
    response_http_status integer NOT NULL,
    response_headers jsonb NOT NULL,
    response_body text NOT NULL,
    duration interval NOT NULL
);

CREATE INDEX idx_ccr_soap_log_request_start ON register.ccr_soap_log (request_start);
CREATE INDEX idx_ccr_soap_log_response_http_status ON register.ccr_soap_log (response_http_status);
CREATE INDEX idx_ccr_soap_log_duration ON register.ccr_soap_log (duration);
