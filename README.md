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

Or in Docker:

```bash
docker build -t vastora-api .
docker run --rm -p 8080:8080 --env-file .env vastora-api
```

Tests: `dotnet test` from the repo root.

Swagger UI: `http://localhost:<port>/swagger`. On first run, a `PlatformSuperAdmin` account
is auto-seeded — check the startup logs for its (generated, if not configured) password.

## Layout

```
src/
  Vastora.Domain          entities, enums
  Vastora.Application     services, DTOs, validators, interfaces
  Vastora.Infrastructure  MongoDB, JWT, password hashing, webhooks
  Vastora.API             controllers, middleware, filters, Program.cs
tests/
  Vastora.Application.Tests   unit tests over the business-rule-heavy services
```

## What it does

128 endpoints across four realms. Beyond the basics — multi-tenancy, catalog, cart, orders,
inventory, accounting — it covers tax, shipping zones, returns and partial refunds, product
variants, a promotions engine, gift cards and store credit, reviews, wishlists, guest checkout,
faceted search, storefront content, COGS-based accounting, per-business analytics, invoicing,
email verification, audit logging, lifecycle notifications, data-rights endpoints and tenant
webhooks. See §7 and §9 of the blueprint for the full inventory and the honest list of what is
still missing.
