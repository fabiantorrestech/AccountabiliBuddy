# The sealed relay (ADR 46). Build from the repository root:
#   docker build -f deploy/relay/Dockerfile -t accountabilibuddy-relay .
# Data (the stored sealed files and, in pinned mode, relay.pfx) lives in the /data volume.
#
# Keeping it patched: the image carries its own .NET runtime, and "restart: unless-stopped" never rebuilds it. Rebuild it on
# every AccountabiliBuddy release and about once a month (.NET security fixes ship on the second Tuesday), pulling the
# newest base images:
#   docker compose build --pull && docker compose up -d
# The relay logs its build and runtime at start ("docker compose logs relay"); "docker compose run --rm relay --version" prints them.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/AccountabiliBuddy.Relay/ src/AccountabiliBuddy.Relay/
RUN dotnet publish src/AccountabiliBuddy.Relay/AccountabiliBuddy.Relay.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Run as the image's non-root "app" user ($APP_UID is set by the official image); give it the data folder before the
# volume is declared so a new volume starts out owned by it.
RUN mkdir /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8743
ENTRYPOINT ["dotnet", "AccountabiliBuddy.Relay.dll", "--data", "/data"]
