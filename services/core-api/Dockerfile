# syntax=docker/dockerfile:1.7
# ---- builder ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS builder
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
WORKDIR /src

COPY *.sln Directory.Build.props ./
COPY FjordWatch.Domain/*.csproj         ./FjordWatch.Domain/
COPY FjordWatch.Infrastructure/*.csproj ./FjordWatch.Infrastructure/
COPY FjordWatch.Agent/*.csproj          ./FjordWatch.Agent/
COPY FjordWatch.Api/*.csproj            ./FjordWatch.Api/
COPY FjordWatch.Api.Tests/*.csproj      ./FjordWatch.Api.Tests/
RUN dotnet restore FjordWatch.Api/FjordWatch.Api.csproj

COPY . .
RUN dotnet publish FjordWatch.Api/FjordWatch.Api.csproj \
        -c Release \
        -o /app \
        --no-restore \
 && rm -rf /app/*.pdb

# ---- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system --gid 10001 app \
 && useradd  --system --uid 10001 --gid app --no-create-home --shell /usr/sbin/nologin app

WORKDIR /app
COPY --from=builder /app .
USER app:app

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --silent --fail http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "FjordWatch.Api.dll"]
