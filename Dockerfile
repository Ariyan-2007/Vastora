# §9.40. Multi-stage so the runtime image carries no SDK, no source and no build cache.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, restored on their own layer: this only re-runs when a .csproj changes,
# not on every source edit.
COPY Vastora.slnx ./
COPY src/Vastora.Domain/Vastora.Domain.csproj src/Vastora.Domain/
COPY src/Vastora.Application/Vastora.Application.csproj src/Vastora.Application/
COPY src/Vastora.Infrastructure/Vastora.Infrastructure.csproj src/Vastora.Infrastructure/
COPY src/Vastora.API/Vastora.API.csproj src/Vastora.API/
COPY tests/Vastora.Application.Tests/Vastora.Application.Tests.csproj tests/Vastora.Application.Tests/
RUN dotnet restore src/Vastora.API/Vastora.API.csproj

COPY . .
RUN dotnet publish src/Vastora.API/Vastora.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root: a container that doesn't need to write outside its own upload directory shouldn't
# be able to.
RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin vastora
COPY --from=build /app ./

# LocalFileStorageService writes here (§9.5). Still local disk — a real deployment should mount a
# volume or, better, swap in an S3/Azure Blob IFileStorageService, since this does not survive a
# redeploy and is not shared across instances.
RUN mkdir -p /app/wwwroot/uploads && chown -R vastora:vastora /app/wwwroot
USER vastora

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# Uses the real /health endpoint, which pings MongoDB rather than only confirming the process
# is up (§9.11) — so an orchestrator restarts a pod that can't reach its database.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "Vastora.API.dll"]
