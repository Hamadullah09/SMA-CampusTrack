# ADR-0001 — EF Core line and MySQL provider

**Status:** Accepted · 2026-08-20

## Context

The product targets .NET 10 and MySQL. That combination has one awkward corner: which
EF Core provider to use.

Two candidates were evaluated empirically against MySQL 8.4, not chosen on reputation.

## Options tested

### Oracle `MySql.EntityFrameworkCore` 10.0.9

The only provider with a stable release built for EF Core 10, so it would keep the entire
stack on the current line.

Verified working: model building, Identity tables, migration generation, `DateOnly` → `date`,
`TimeOnly` → `time`, decimal precision, utf8mb4.

**Then it failed on read-back:**

```
System.InvalidCastException: Unable to cast object of type 'System.TimeSpan'
to type 'System.TimeOnly'.
   at MySql.Data.MySqlClient.MySqlDataReader.GetFieldValue[T](Int32 ordinal)
```

The provider writes a `TimeOnly` correctly and cannot materialise it back.

### Pomelo `Pomelo.EntityFrameworkCore.MySql` 9.0.0 (EF Core 9)

The same test suite passed completely: migration applied, insert, `TimeOnly`/`DateOnly`
round-trip, `GROUP BY` aggregation, Identity.

## Decision

**Pomelo 9.0.0 on the EF Core 9 line.** The applications target `net10.0` and use the
ASP.NET Core 10 framework; only the data-access packages sit on the 9 line.

## Rationale

This product is timetable-driven. `TimeOnly` is not incidental: bell times, period
boundaries, lateness thresholds and the entire RFID-to-lesson matching depend on reading
times back out of the database. A provider that cannot do that is not usable here,
regardless of which EF version it targets.

Mixing an EF Core 9 data layer with an ASP.NET Core 10 application is a supported
configuration — EF Core 9 targets `net8.0` and runs unchanged on .NET 10.

## Consequences

- The API, web app and everything else are fully on .NET 10.
- `Directory.Packages.props` pins the data-access group to 9.0.11 with a comment pointing here.
- Provider choice is confined to one `UseMySql` call in `DependencyInjection.cs`, so moving
  to Pomelo's EF Core 10 release when it ships is a version bump and a test run.

## Revisit when

Pomelo publishes a stable EF Core 10 release, **or** Oracle's provider fixes the `TimeOnly`
materialisation defect. Re-run `backend/tests/CampusTrack.UnitTests` plus the integration
suite against a real MySQL before switching.
