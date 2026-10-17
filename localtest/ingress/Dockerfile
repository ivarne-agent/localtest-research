FROM docker.io/library/nginx:1.27-alpine
COPY templates/ /etc/nginx/templates/
COPY docker-entrypoint.d/ /docker-entrypoint.d/
ENV REGISTER_UPSTREAM=http://register:5020 \
    AUTHENTICATION_UPSTREAM=http://authentication:5101 \
    AUTHORIZATION_UPSTREAM=http://authorization:5050 \
    ACCESS_MANAGEMENT_UPSTREAM=http://access-management:5080 \
    STORAGE_UPSTREAM=http://storage:5010 \
    APP_UPSTREAM=http://host.docker.internal:5005 \
    PDF_UPSTREAM=http://pdf3:5031 \
    WORKFLOW_ENGINE_UPSTREAM=http://workflow-engine:9090
EXPOSE 8000
