# SMA Campus Track

An RFID-first school management platform. UHF readers at gates and classroom doors produce
the movement record; attendance, parent notifications and reporting are derived from it
rather than typed in.

Four experiences over one API: **Admin** and **Teacher** on the web, **Student** and
**Parent** on mobile.

---

## What it does

- **Knows who is on site.** Gate readers move students on and off campus; the dashboard
  answers "who is in the building" as an index lookup, not a scan.
- **Derives attendance from evidence.** Arrival time decides lateness against the configured
  school day; classroom entry and exit decide whether enough of a lesson was attended.
  A teacher can still correct anything — with a reason, recorded.
- **Tells parents at the moment it matters.** *"Your child Ahmed entered the school at
  7:48 AM."* Push plus a stored inbox, so a missed banner is not a missed message.
- **Keeps a full audit trail.** Every change records who, what, before, after, from where.
- **Runs the school around it.** Classes, sections, subjects, timetable, assignments,
  quizzes, exams, grades, announcements, leave, reports with CSV/Excel/PDF export.

---

## Stack

| Layer | Technology |
|---|---|
| API | .NET 10 · ASP.NET Core · Clean Architecture · SignalR |
| Data | MySQL 8.4 · EF Core (Pomelo — see [ADR-0001](docs/architecture/adr-0001-mysql-provider.md)) |
| Auth | ASP.NET Core Identity · JWT with rotating refresh tokens · 121 granular permissions |
| Web | React 18 · TypeScript · Vite · TanStack Query · Recharts |
| Mobile | Flutter · Riverpod · Dio · secure token storage · FCM |
| Deploy | Docker Compose · GitHub Actions |

---

## Running it

### Prerequisites

.NET 10 SDK · Node 20+ · Docker (for MySQL) · Flutter 3.24+ (mobile only)

### 1. Database

```bash
docker run -d --name campustrack-mysql \
  -e MYSQL_ROOT_PASSWORD=CampusTrack_Root_2026 \
  -e MYSQL_DATABASE=campustrack \
  -e MYSQL_USER=campustrack \
  -e MYSQL_PASSWORD=CampusTrack_Dev_2026 \
  -p 3307:3306 mysql:8.4
```

### 2. API

```bash
cd backend/src/CampusTrack.Api
dotnet run --urls http://localhost:5080
```

Migrations apply and the baseline seeds automatically in development: the school record,
121 permissions, six roles, 33 settings, a grading scale and the first administrator.

- API: `http://localhost:5080` · Swagger: `/swagger`
- Sign in: `admin` / `Admin@2026` (change it immediately)

### 3. Web portal

```bash
cd web
npm install
npm run dev
```

`http://localhost:5173` — the dev server proxies `/api` and `/hubs` to the API, so the
browser sees one origin.

### 4. Mobile app

```bash
cd mobile/campustrack_app
flutter create .        # generates the android/ and ios/ platform folders
flutter pub get
flutter run
```

Point it at your machine with `--dart-define=API_BASE_URL=http://10.0.2.2:5080`
(`10.0.2.2` reaches the host from an Android emulator).

### Everything at once

```bash
cp .env.example .env     # then set real secrets
docker compose up -d
```

---

## Trying the RFID flow without hardware

The simulator injects reads through the **same queue** as a physical reader, so
notifications and attendance behave exactly as they will on site.

1. Create a student and assign a card (Students → Add student, or RFID → Cards).
2. Create a location and a reader (RFID → Locations, then Readers).
3. Live monitor → **Simulate a pass**.

Within a few seconds: a movement event appears on the dashboard live feed, attendance is
written, presence flips to on-site, and the parent's notification is queued.

```bash
curl -X POST http://localhost:5080/api/v1/rfid/simulate \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"deviceId":"MAIN-GATE-R01","epc":"E28011606000020C3F1A2B3C","direction":"Entry","readsPerAntenna":5}'
```

---

## Layout

```
backend/
  src/CampusTrack.Domain/          entities, enums, invariants — no infrastructure
  src/CampusTrack.Application/     contracts, RFID engine (pure), permission catalogue
  src/CampusTrack.Infrastructure/  EF Core, Identity, RFID pipeline, notifications, reports
  src/CampusTrack.Api/             controllers, middleware, SignalR hub
  src/CampusTrack.RfidGateway/     on-site translator for readers that cannot POST JSON
  tests/                           unit and integration tests
web/                               Admin + Teacher portals
mobile/campustrack_app/            Student + Parent apps
docs/                              architecture, ADRs, RFID integration
legacy/prototype-v1/               the previous .NET 8 / SQL Server prototype, preserved
```

---

## Testing

```bash
cd backend && dotnet test              # 50 unit tests
cd web && npx tsc -b                   # type check
cd mobile/campustrack_app && flutter test
```

The unit tests concentrate on the decisions that would be expensive to get wrong: every
branch of direction resolution including the ambiguous cases that must produce *no* event,
the sequence buffer's timing and its exactly-once guarantee under concurrent sweeps, EPC
normalisation, and the permission catalogue (a regression there would let a parent edit the
school roll).

---

## Documentation

| Document | Covers |
|---|---|
| [Architecture](docs/architecture/overview.md) | Layering, and the reasoning behind each decision |
| [ADR-0001](docs/architecture/adr-0001-mysql-provider.md) | Why EF Core 9 + Pomelo on a .NET 10 stack |
| [RFID integration](docs/rfid-integration.md) | Wiring, device auth, the wire format, tuning, failure handling |
| Swagger (`/swagger`) | Every endpoint, live |

---

## Security

- Refresh tokens are stored hashed and rotate on use; replaying a consumed token revokes the
  whole family, turning a stolen token into a detectable event.
- Device keys are per-reader, stored as SHA-256, compared in constant time, shown once.
- Authorisation is by permission, never by role name. Guardian access is bounded by approved
  child links, and academic detail by a separate flag on that link.
- Full EPCs never reach a list screen, a log or a mobile client — only the last six characters.
- Rate limiting is tightest on sign-in and generous but per-device on RFID ingestion.
- The API refuses to start outside development if the JWT signing key is still the placeholder.

---

## Status

Built and verified end to end: schema applied to real MySQL (75 tables), the full
RFID→attendance→notification→dashboard loop exercised with live data, all API endpoints
returning 200, exports producing valid CSV/XLSX/PDF, and the web portal driven in a browser
with SignalR pushing live movement.

See the handover notes for what is complete, what is scaffolded, and what a school would
need before going live.
