# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first so `dotnet restore` is cached and does not re-run
# on every source change. This is the difference between a 20-second and a 4-minute build.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/CampusTrack.Domain/CampusTrack.Domain.csproj", "src/CampusTrack.Domain/"]
COPY ["src/CampusTrack.Application/CampusTrack.Application.csproj", "src/CampusTrack.Application/"]
COPY ["src/CampusTrack.Infrastructure/CampusTrack.Infrastructure.csproj", "src/CampusTrack.Infrastructure/"]
COPY ["src/CampusTrack.Api/CampusTrack.Api.csproj", "src/CampusTrack.Api/"]
RUN dotnet restore "src/CampusTrack.Api/CampusTrack.Api.csproj"

COPY . .
RUN dotnet publish "src/CampusTrack.Api/CampusTrack.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Runs as a non-root user. A container that is compromised should not also be root.
RUN adduser --disabled-password --gecos "" --uid 10001 campustrack \
 && mkdir -p /app/storage /app/logs \
 && chown -R campustrack:campustrack /app
USER campustrack

COPY --from=build --chown=campustrack:campustrack /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

# Uses the readiness probe so the orchestrator only routes traffic once the database
# is actually reachable, not merely when the process has started.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD ["dotnet", "CampusTrack.Api.dll", "--healthcheck"] || exit 1

ENTRYPOINT ["dotnet", "CampusTrack.Api.dll"]
