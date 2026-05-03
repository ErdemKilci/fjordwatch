# syntax=docker/dockerfile:1.7
# ---- builder ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS builder
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
WORKDIR /src

COPY FjordWatch.Web/*.csproj ./FjordWatch.Web/
COPY *.sln ./
RUN dotnet restore FjordWatch.Web/FjordWatch.Web.csproj

COPY . .
RUN dotnet publish FjordWatch.Web/FjordWatch.Web.csproj \
        -c Release \
        -o /publish \
        --no-restore

# ---- runtime ---------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

RUN addgroup -S app && adduser -S -G app app \
 && mkdir -p /var/cache/nginx /var/run \
 && chown -R app:app /var/cache/nginx /var/run /usr/share/nginx/html /etc/nginx /var/log/nginx

COPY nginx.conf /etc/nginx/nginx.conf
COPY --from=builder /publish/wwwroot /usr/share/nginx/html

USER app:app
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=5s --retries=3 \
    CMD wget --quiet --spider http://127.0.0.1:8080/ || exit 1
