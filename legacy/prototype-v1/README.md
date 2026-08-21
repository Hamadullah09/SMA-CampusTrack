# CampusTrack — RFID School/College Management Suite

End-to-end system for schools & colleges: UHF-RFID gate and room attendance,
full-semester timetables, teacher progress reports, parent feedback, student
project/thesis uploads, QR-code assignment downloads, and push notifications
to parents.

```
├── database/                MS SQL Server schema + demo seed data
├── backend/                 ASP.NET Core 8 Web API + teacher/admin web portal
│   └── src/CampusTrack.Api/
├── mobile/campustrack_app/  Flutter app (Android + iOS) — parent & student portals
└── tools/rfid-simulator/    PowerShell script that fakes reader traffic for testing
```

---

## How the RFID attendance logic works

Students carry ID cards with UHF RFID tags (the tag EPC is stored on the
student record). Fixed readers push every antenna hit to
`POST /api/rfid/reads`. The backend buffers hits per *(reader, tag)* and,
once the tag goes quiet for 4 seconds, resolves the direction from the
**antenna order**:

| Location | Antennas | Entry sequence | Exit sequence |
|---|---|---|---|
| School gate | 3 | 1 → 2 → 3 | 3 → 2 → 1 |
| Classrooms, labs, library, discussion rooms, auditorium | 2 | 1 → 2 | 2 → 1 |

Implementation details (`Services/RfidSequenceEngine.cs`):
- Repeated hits on the same antenna are collapsed (UHF readers report a tag
  many times per second).
- A sequence seen on only one antenna, or that starts and ends on the same
  antenna (student walked up and turned back), is discarded as ambiguous.
- Identical events (same student/room/direction) within 60 s are suppressed.
- Every raw hit is kept in `RawRfidReads` for audit/re-processing.

**Gate events push an instant notification to the parent's phone.** All
events (gate + rooms) feed the **daily summary (18:00)** and **weekly
summary (Friday)** notifications — times configurable in `appsettings.json`.

## Feature map

| Requirement | Where |
|---|---|
| Parent: semester class schedules | App → Schedule tab, `GET /api/schedule/student/{id}` |
| Parent: activity reports | App → Activity tab |
| Parent: feedback in fixed categories | App → Feedback tab (`Teaching, Homework, Facilities, Transport, Discipline, Suggestion, Complaint`) |
| Parent: gate entry/exit push + daily/weekly summaries | FCM push + Alerts tab |
| Student: upload projects/activities/theses | App → My work tab |
| Student: QR download of assignments & notes | App → Assignments tab → *Scan QR* |
| Teacher portal: progress updates, feedback replies, upload reviews, assignment publishing | Web portal at the API root URL |
| Admin: modular classes/sections/rooms/readers, accounts, timetable | Same portal (Administration tab, admin login) |
| Whole-day room-by-room activity record | App → Attendance → Movements |

## Getting started

### 1. Database (SQL Server)
Either run `database/01_schema.sql` (+ optional `02_seed.sql`), **or** just
point the API at a server — it creates the schema itself on first run
(EF Core `EnsureCreated`).

### 2. Backend
```bash
cd backend/src/CampusTrack.Api
dotnet run
```
- Edit `appsettings.json` first: connection string, `Jwt:Key` (any long
  random string), `Rfid:ApiKey` (shared secret the readers send),
  `Fcm:ServerKey` (Firebase project → Cloud Messaging) and `PublicBaseUrl`
  (the URL phones can reach, used inside QR codes).
- First run seeds an admin account: **admin / Admin@123** — change it.
- Teacher/Admin portal: `http://localhost:5000/` · Swagger: `/swagger`.

### 3. Mobile app
```bash
cd mobile/campustrack_app
flutter create .          # generates android/ & ios/ platform folders
flutter pub get
flutter run
```
- Set `kApiBaseUrl` in `lib/core/api_client.dart` to your server address
  (`http://10.0.2.2:5000` reaches localhost from the Android emulator).
- Push notifications: `flutterfire configure`, then rebuild. The app runs
  fine without Firebase — notifications still appear in the Alerts tab.
- One app serves both roles: parents and students see different portals
  after login.

### 4. Simulate a walk-through (no hardware needed)
```bash
powershell -File tools/rfid-simulator/Simulate-Walk.ps1 -ReaderCode GATE-01 -Epc E20034120001 -Direction Entry -Antennas 3
```
Create a student with EPC `E20034120001` in the portal first; ~4 s later an
Entry event appears and the parent gets the notification.

### Connecting real readers
Most fixed UHF readers (Impinj, Zebra, Chainway, …) can push tag reports to
an HTTP endpoint directly or via a small middleware using the vendor SDK /
LLRP. Map each report to:
```json
POST /api/rfid/reads      (header X-Reader-ApiKey: <shared key>)
{ "reads": [ { "readerCode": "GATE-01", "antennaNo": 2,
               "epc": "E20034120001", "readTime": "2026-08-17T08:01:03Z" } ] }
```

## Default logins
| Role | Where | Credentials |
|---|---|---|
| Admin | Web portal | `admin` / `Admin@123` (seeded; change immediately) |
| Teacher / Parent / Student | created by admin in the portal | — |

## Tech stack
- **Backend:** ASP.NET Core 8, EF Core (SQL Server), JWT auth, QRCoder,
  hosted background services (RFID sweep, summary scheduler), FCM push.
- **Mobile:** Flutter (Material 3) — Android & iOS from one codebase;
  `mobile_scanner` for QR, `file_picker` for uploads, `firebase_messaging`
  for push.
- **Portal:** dependency-free single-page HTML/JS served from `wwwroot`.
