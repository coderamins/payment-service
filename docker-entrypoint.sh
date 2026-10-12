#!/bin/sh
set -e

# Trim whitespace/CRLF and strip surrounding quotes from env values.
sanitize() {
  printf '%s' "$1" | tr -d '\r' | sed \
    -e 's/^[[:space:]]*//' \
    -e 's/[[:space:]]*$//' \
    -e 's/^\\"\(.*\)\\"$/\1/' \
    -e 's/^"\(.*\)"$/\1/' \
    -e "s/^'\\(.*\\)'$/\\1/" \
    -e 's/^"\(.*\)"$/\1/' \
    -e "s/^'\\(.*\\)'$/\\1/"
}

echo "APP_VERSION=${APP_VERSION:-unknown}"

ASPNETCORE_URLS="$(sanitize "${ASPNETCORE_URLS:-http://+:8080}")"
case "$ASPNETCORE_URLS" in
  \"*|\'*|*\"|*\' )
    echo "WARN: ASPNETCORE_URLS still had quotes; forcing http://+:8080"
    ASPNETCORE_URLS="http://+:8080"
    ;;
  http://*|https://*) ;;
  *)
    echo "WARN: ASPNETCORE_URLS='$ASPNETCORE_URLS' invalid; forcing http://+:8080"
    ASPNETCORE_URLS="http://+:8080"
    ;;
esac
export ASPNETCORE_URLS
unset ASPNETCORE_HTTP_PORTS ASPNETCORE_HTTPS_PORTS 2>/dev/null || true
echo "ASPNETCORE_URLS=$ASPNETCORE_URLS"

# JWT public key for OpenIddict validation
DEFAULT_PUBLIC_KEY="/app/Keys/public_key.der"
if [ ! -f "$DEFAULT_PUBLIC_KEY" ] && [ -f "/app/keys/public_key.der" ]; then
  mkdir -p /app/Keys
  cp -f /app/keys/public_key.der /app/Keys/public_key.der
fi
if [ -n "${OPENIDDICT_PUBLIC_KEY_PATH:-}" ] && [ -f "$OPENIDDICT_PUBLIC_KEY_PATH" ]; then
  mkdir -p /app/Keys
  cp -f "$OPENIDDICT_PUBLIC_KEY_PATH" /app/Keys/public_key.der
  echo "Using JWT public key from: $OPENIDDICT_PUBLIC_KEY_PATH"
elif [ -f "$DEFAULT_PUBLIC_KEY" ]; then
  echo "Using JWT public key: $DEFAULT_PUBLIC_KEY"
else
  echo "WARN: Keys/public_key.der not found — JWT validation may fail at runtime."
fi

if [ -n "${APP_NAME:-}" ]; then
  export AppOptions__Name="$(sanitize "$APP_NAME")"
fi

# SQL → SqlServerOptions (ADO.NET / BaseNativeRepository)
if [ -n "${SQL_CONNECTION_STRING:-}" ]; then
  SQL_CONNECTION_STRING="$(sanitize "$SQL_CONNECTION_STRING")"
  echo "SQL_CONNECTION_STRING length=${#SQL_CONNECTION_STRING}"

  case "$SQL_CONNECTION_STRING" in
    \"*|\'*|*\"|*\' )
      echo "ERROR: SQL_CONNECTION_STRING still contains quotes after sanitize. Use --env-file without extra quotes."
      exit 1
      ;;
  esac

  if [ "${#SQL_CONNECTION_STRING}" -lt 20 ]; then
    echo "ERROR: SQL_CONNECTION_STRING looks too short/invalid."
    exit 1
  fi

  export SqlServerOptions__ConnectionString="$SQL_CONNECTION_STRING"

  if [ "${LOG_DB_ENABLED:-}" = "true" ] || [ -n "${LOG_DB_CONNECTION_STRING:-}" ]; then
    LOG_DB_CS="$(sanitize "${LOG_DB_CONNECTION_STRING:-$SQL_CONNECTION_STRING}")"
    export LogOptions__Database__ConnectionString="$LOG_DB_CS"
  fi
fi

# Identity UserManagment gRPC → IdentityGrpcOptions
if [ -n "${IDENTITY_GRPC_ADDRESS:-}" ]; then
  export IdentityGrpcOptions__Address="$(sanitize "$IDENTITY_GRPC_ADDRESS")"
  echo "IdentityGrpcOptions.Address=$IdentityGrpcOptions__Address"
fi

# RabbitMQ → RabbitMqOptions
if [ -n "${RABBITMQ_HOST:-}" ]; then export RabbitMqOptions__HostName="$(sanitize "$RABBITMQ_HOST")"; fi
if [ -n "${RABBITMQ_PORT:-}" ]; then export RabbitMqOptions__Port="$(sanitize "$RABBITMQ_PORT")"; fi
if [ -n "${RABBITMQ_USER:-}" ]; then export RabbitMqOptions__UserName="$(sanitize "$RABBITMQ_USER")"; fi
if [ -n "${RABBITMQ_PASSWORD:-}" ]; then export RabbitMqOptions__Password="$(sanitize "$RABBITMQ_PASSWORD")"; fi
if [ -n "${RABBITMQ_EXCHANGE:-}" ]; then export RabbitMqOptions__ExchangeName="$(sanitize "$RABBITMQ_EXCHANGE")"; fi
if [ -n "${RABBITMQ_VHOST:-}" ]; then export RabbitMqOptions__VirtualHost="$(sanitize "$RABBITMQ_VHOST")"; fi
if [ -n "${RABBITMQ_USE:-}" ]; then export RabbitMqOptions__UseRabbitMq="$(sanitize "$RABBITMQ_USE")"; fi
if [ -n "${RABBITMQ_USER:-}" ] || [ -n "${RABBITMQ_HOST:-}" ]; then
  echo "RabbitMQ config: host=${RABBITMQ_HOST:-} port=${RABBITMQ_PORT:-5672} user=${RABBITMQ_USER:-} vhost=${RABBITMQ_VHOST:-/} use=${RABBITMQ_USE:-}"
fi

# OpenIddict JWT validation → OpenIdditOptions
if [ -n "${OPENIDDICT_MAIN_ISSUER:-}" ]; then
  export OpenIdditOptions__MainIssuer="$(sanitize "$OPENIDDICT_MAIN_ISSUER")"
  echo "OpenIdditOptions.MainIssuer=$OpenIdditOptions__MainIssuer"
fi

if [ -n "${OPENIDDICT_VALID_ISSUERS:-}" ]; then
  ISSUERS="$(sanitize "$OPENIDDICT_VALID_ISSUERS")"
  i=0
  OLD_IFS=$IFS
  IFS=','
  for issuer in $ISSUERS; do
    issuer="$(sanitize "$issuer")"
    if [ -n "$issuer" ]; then
      export "OpenIdditOptions__ValidIssuers__$i=$issuer"
      i=$((i + 1))
    fi
  done
  IFS=$OLD_IFS
  echo "OpenIddit ValidIssuers count=$i"
fi

# CORS — comma-separated → AllowCORS__Url__*
if [ -n "${ALLOW_CORS_URLS:-}" ]; then
  URLS="$(sanitize "$ALLOW_CORS_URLS")"
  i=0
  OLD_IFS=$IFS
  IFS=','
  for url in $URLS; do
    url="$(sanitize "$url")"
    if [ -n "$url" ]; then
      export "AllowCORS__Url__$i=$url"
      i=$((i + 1))
    fi
  done
  IFS=$OLD_IFS
fi

if [ -n "${HEALTH_ENABLED:-}" ]; then export HealthOptions__Enabled="$(sanitize "$HEALTH_ENABLED")"; fi
if [ -n "${SWAGGER_ENABLED:-}" ]; then export OpenApi__Enabled="$(sanitize "$SWAGGER_ENABLED")"; fi

# Logging
if [ -n "${LOG_LEVEL:-}" ]; then export LogOptions__Level="$(sanitize "$LOG_LEVEL")"; fi
if [ -n "${LOG_CONSOLE_ENABLED:-}" ]; then export LogOptions__Console__Enabled="$(sanitize "$LOG_CONSOLE_ENABLED")"; fi
if [ -n "${LOG_FILE_ENABLED:-}" ]; then export LogOptions__File__Enabled="$(sanitize "$LOG_FILE_ENABLED")"; fi
if [ -n "${LOG_FILE_PATH:-}" ]; then export LogOptions__File__Path="$(sanitize "$LOG_FILE_PATH")"; fi
if [ -n "${LOG_DB_ENABLED:-}" ]; then export LogOptions__Database__Enabled="$(sanitize "$LOG_DB_ENABLED")"; fi
if [ -n "${LOG_DB_CONNECTION_STRING:-}" ]; then export LogOptions__Database__ConnectionString="$(sanitize "$LOG_DB_CONNECTION_STRING")"; fi
if [ -n "${LOG_ELASTIC_ENABLED:-}" ]; then export LogOptions__Elasticsearch__Enabled="$(sanitize "$LOG_ELASTIC_ENABLED")"; fi
if [ -n "${LOG_ELASTIC_URIS:-}" ]; then export LogOptions__Elasticsearch__NodeUris="$(sanitize "$LOG_ELASTIC_URIS")"; fi
if [ -n "${LOG_ELASTIC_INDEX:-}" ]; then export LogOptions__Elasticsearch__IndexFormat="$(sanitize "$LOG_ELASTIC_INDEX")"; fi
if [ -n "${LOG_SPLUNK_ENABLED:-}" ]; then export LogOptions__Splunk__Enabled="$(sanitize "$LOG_SPLUNK_ENABLED")"; fi
if [ -n "${LOG_SPLUNK_HOST:-}" ]; then export LogOptions__Splunk__Host="$(sanitize "$LOG_SPLUNK_HOST")"; fi
if [ -n "${LOG_SPLUNK_TOKEN:-}" ]; then export LogOptions__Splunk__Token="$(sanitize "$LOG_SPLUNK_TOKEN")"; fi
if [ -n "${LOG_SPLUNK_INDEX:-}" ]; then export LogOptions__Splunk__Index="$(sanitize "$LOG_SPLUNK_INDEX")"; fi

# OpenTelemetry
if [ -n "${OTEL_METRICS_ENABLED:-}" ]; then export ObservabilityOptions__MetricsEnabled="$(sanitize "$OTEL_METRICS_ENABLED")"; fi
if [ -n "${OTEL_TRACING_ENABLED:-}" ]; then export ObservabilityOptions__TracingEnabled="$(sanitize "$OTEL_TRACING_ENABLED")"; fi
if [ -n "${OTEL_LOGGING_ENABLED:-}" ]; then export ObservabilityOptions__LoggingEnabled="$(sanitize "$OTEL_LOGGING_ENABLED")"; fi
if [ -n "${OTEL_USE_OTLP:-}" ]; then export ObservabilityOptions__UseOTLPExporter="$(sanitize "$OTEL_USE_OTLP")"; fi
if [ -n "${OTEL_OTLP_ENDPOINT:-}" ]; then export ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint="$(sanitize "$OTEL_OTLP_ENDPOINT")"; fi
if [ -n "${OTEL_USE_CONSOLE:-}" ]; then export ObservabilityOptions__UseConsoleExporter="$(sanitize "$OTEL_USE_CONSOLE")"; fi
if [ -n "${OTEL_USE_JAEGER:-}" ]; then export ObservabilityOptions__UseJaegerExporter="$(sanitize "$OTEL_USE_JAEGER")"; fi
if [ -n "${OTEL_USE_ZIPKIN:-}" ]; then export ObservabilityOptions__UseZipkinExporter="$(sanitize "$OTEL_USE_ZIPKIN")"; fi
if [ -n "${OTEL_USE_PROMETHEUS:-}" ]; then export ObservabilityOptions__UsePrometheusExporter="$(sanitize "$OTEL_USE_PROMETHEUS")"; fi
if [ -n "${OTEL_SERVICE_NAME:-}" ]; then export ObservabilityOptions__ServiceName="$(sanitize "$OTEL_SERVICE_NAME")"; fi

# gRPC client → Identity over plain HTTP/2
export DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP2UNENCRYPTEDSUPPORT=1

exec dotnet PaymentService.API.dll
