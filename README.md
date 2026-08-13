# Vastora

A universal, multi-tenant e-commerce API platform — sold as a subscription, not built as a
single storefront. Businesses get a Landing Page, Shop, and BackOffice; groups of businesses
under one owner additionally get a SuperOffice spanning all of them.

**Read [`VASTORA_BLUEPRINT.md`](VASTORA_BLUEPRINT.md) first.** It's the full project brief:
the business model, the architecture, the complete domain model, exactly what's built and
verified so far, and the session-by-session roadmap for everything still to come. This
README is just the quick start.

## Stack

.NET 10 · ASP.NET Core Web API · MongoDB · JWT auth · Clean Architecture
(`Domain` / `Application` / `Infrastructure` / `API`)

## Run it

```bash
cp .env.example .env   # then fill in MONGODB_URI and Jwt__Secret
dotnet run --project src/Vastora.API/Vastora.API.csproj
```

Swagger UI: `http://localhost:<port>/swagger`. On first run, a `PlatformSuperAdmin` account
is auto-seeded — check the startup logs for its (generated, if not configured) password.

## Layout

```
src/
  Vastora.Domain          entities, enums
  Vastora.Application     services, DTOs, validators, interfaces
  Vastora.Infrastructure  MongoDB, JWT, password hashing
  Vastora.API             controllers, middleware, Program.cs
```
