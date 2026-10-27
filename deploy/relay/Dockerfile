# The sealed relay (ADR 46). Build from the repository root:
#   docker build -f deploy/relay/Dockerfile -t accountabilibuddy-relay .
# Data (the stored sealed files and, in pinned mode, relay.pfx) lives in the /data volume.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/AccountabiliBuddy.Relay/ src/AccountabiliBuddy.Relay/
RUN dotnet publish src/AccountabiliBuddy.Relay/AccountabiliBuddy.Relay.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The official image runs as a non-root user (app); the volume must be writable by it.
VOLUME /data
EXPOSE 8743
ENTRYPOINT ["dotnet", "AccountabiliBuddy.Relay.dll", "--data", "/data"]
