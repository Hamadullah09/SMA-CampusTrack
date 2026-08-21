# Architecture

## Shape

```
   Admin portal        Teacher portal        Student app        Parent app
   (React SPA)         (React SPA)           (Flutter)          (Flutter)
        └───────────────────┴────────────────────┴──────────────────┘
                                    │  HTTPS / JWT
                        ┌───────────▼────────────┐
                        │   CampusTrack.Api      │  ASP.NET Core 10
                        │   controllers · SignalR │
                        └───────────┬────────────┘
                                    │
                        ┌───────────▼────────────┐
                        │ CampusTrack.Infrastructure │  EF Core · Identity · FCM · reports
                        └───────────┬────────────┘
                                    │
                        ┌───────────▼────────────┐
                        │ CampusTrack.Application │  contracts · RFID engine · permissions
                        └───────────┬────────────┘
                                    │
                        ┌───────────▼────────────┐
                        │   CampusTrack.Domain    │  entities · enums · invariants
                        └────────────────────────┘
                                    │
                                  MySQL

   RFID readers ──▶ CampusTrack.RfidGateway ──▶ /api/v1/rfid/reads
```

Dependencies point inward. Domain knows nothing about EF Core, HTTP or Firebase.

## Why the layers are drawn here

**Domain** holds entities and the rules that are true regardless of storage — that a
reader is stale after three missed heartbeats, that a tag is usable only when active and
assigned. Its only dependency is the Identity primitive `ApplicationUser` derives from.

**Application** holds contracts and the logic that is pure: `DirectionResolver` and
`TagSequenceBuffer` decide the most consequential thing the product does, and they have no
clock, no database and no I/O. That is what makes the whole decision table testable.

**Infrastructure** implements those contracts. Provider choice, FCM, SMTP, PDF generation
and the DbContext live here, so replacing any of them touches one project.

**Api** is deliberately thin: routing, authorisation attributes, model binding.

## Decisions worth knowing

**Repositories only where they earn their place.** EF Core is already a unit of work and a
repository. Wrapping every entity in a hand-written repository would have cost the
composable filtering and projection every list screen depends on. `IApplicationDbContext`
exposes DbSets; a real repository appears in the RFID ingestion path, where the queue and
buffer genuinely are a different abstraction.

**Auditing is an interceptor, not a service call.** An audit trail that depends on
developers remembering to write to it is an audit trail with holes. Everything reaching
`SaveChanges` is recorded, including background jobs.

**Soft deletion, applied by convention.** Deleting a student would orphan the attendance and
movement history that legitimately points at them. Deletes become updates, a global query
filter hides them, and a second convention extends that filter to child rows that have no
`IsDeleted` column of their own.

**Ingestion is decoupled from processing.** The reader endpoint authenticates, validates and
enqueues — nothing more. A gate reader that waits on database writes and push notifications
falls behind during the morning rush, and a backed-up reader loses reads. The queue is
bounded on purpose: an unbounded queue relocates the failure to memory exhaustion.

**Permissions, never role names.** 121 permissions are discovered by reflection and seeded.
Endpoints name a permission; a school can invent "Head of Year" without a code change.
SuperAdmin passes by role as a break-glass path a school cannot lock itself out of.

**Times are UTC in storage, local in meaning.** A gate event at 23:30 UTC may belong to the
next school day in Riyadh. `IDateTimeProvider` centralises that conversion, which also makes
a full school day replayable in tests.

## Request paths

**A person signs in** → credentials verified → permissions resolved (role grants + user
grants − user denials) → short-lived access token minted with those permissions as claims →
rotating refresh token stored hashed.

**A card is read** → device key authenticated → EPC normalised, RSSI filtered, clock skew
clamped → enqueued → HTTP returns. Off the request path: raw reads batched to MySQL, the
sequence buffered until quiet, direction resolved, duplicates suppressed, event classified
against the timetable, attendance updated, guardians notified, dashboards pushed.

## Scale

The write-heavy table is `rfid_raw_reads`: one row per antenna hit, dozens per second per
tag. It carries exactly four indexes, matched to the four real access patterns, because
every extra index is a write cost. Reads are batched with a periodic flush, old rows are
pruned on a retention window, and resolved events — the rows everything else reads — live in
a separate, far smaller table.
