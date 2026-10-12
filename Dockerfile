# =========================
# Build
# =========================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG VERSION=0.0.0

WORKDIR /src

# Copy project files first
# This keeps NuGet restore cached when only source code changes.
COPY ["src/PaymentService.Domain/PaymentService.Domain.csproj", "src/PaymentService.Domain/"]
COPY ["src/PaymentService.Application/PaymentService.Application.csproj", "src/PaymentService.Application/"]
COPY ["src/PaymentService.Infrastructure/PaymentService.Infrastructure.csproj", "src/PaymentService.Infrastructure/"]
COPY ["src/PaymentService.API/PaymentService.API.csproj", "src/PaymentService.API/"]

# Restore dependencies
RUN dotnet restore "src/PaymentService.API/PaymentService.API.csproj"

# Copy source code
COPY src/ ./src/

# Publish application + version + public key
RUN dotnet publish "src/PaymentService.API/PaymentService.API.csproj" \
        -c Release \
        -o /app/publish \
        --no-restore \
        --no-self-contained \
        -p:Version=$VERSION \
        -p:InformationalVersion=$VERSION \
    && mkdir -p /app/publish/Keys \
    && if [ -f src/PaymentService.API/keys/public_key.der ]; then \
        cp src/PaymentService.API/keys/public_key.der /app/publish/Keys/public_key.der; \
    fi \
    && echo "$VERSION" > /app/publish/version.txt


# =========================
# Runtime
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

ARG VERSION=0.0.0

WORKDIR /app

ENV APP_VERSION=$VERSION \
    IMAGE_VERSION=$VERSION \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    APP_NAME=Payment-Service

EXPOSE 8080

# Copy published application
COPY --from=build /app/publish .

# Entrypoint
COPY docker-entrypoint.sh .

RUN sed -i 's/\r$//' docker-entrypoint.sh \
    && chmod +x docker-entrypoint.sh \
    && mkdir -p Keys logs

ENTRYPOINT ["./docker-entrypoint.sh"]