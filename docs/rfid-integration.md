# RFID integration guide

How UHF readers connect to CampusTrack, what they send, and how the server turns raw
antenna hits into attendance and parent notifications.

---

## 1. Architecture

```
D2184 reader ──┐
D2184 reader ──┼── local network ──▶ RFID gateway ──▶ HTTPS ──▶ CampusTrack API ──▶ MySQL
D2184 reader ──┘                     (optional)                       │
                                                                      ├──▶ SignalR (dashboards)
                                                                      └──▶ FCM (parent phones)
```

Readers never need to reach the public internet. Two topologies are supported:

| Topology | When to use | How |
|---|---|---|
| **Direct HTTP** | The reader firmware can POST JSON to a URL | Point it at `/api/v1/rfid/reads` |
| **Local gateway** | The reader speaks LLRP, a vendor SDK, or a raw TCP protocol | Run `CampusTrack.RfidGateway` on the school LAN; it translates and forwards |

The gateway also buffers during an internet outage, which is why it is the recommended
setup for a site whose connection is not reliable.

---

## 2. Device authentication

Readers are devices, not users: they cannot complete an interactive sign-in or refresh a
token. Each is issued its own API key.

**Issuing a key** — Admin portal → RFID → Readers → *Issue key*.

The plaintext key is displayed **once**. Only a SHA-256 hash is stored, so a database leak
cannot be replayed as a device. A key is scoped to a single reader: one lifted from a side
door cannot fabricate movement at the main gate.

Every request carries two headers:

```
X-Device-Id:  MAIN-GATE-R01
X-Device-Key: <the key issued for that reader>
```

---

## 3. Sending reads

`POST /api/v1/rfid/reads`

```json
{
  "deviceId": "MAIN-GATE-R01",
  "batchId": "b3f1c8e2-4a7d-4a1e-9f66-2c8f1e0a55d1",
  "reads": [
    { "epc": "E28011606000020C3F1A2B3C", "antennaNumber": 1, "readAtUtc": "2026-08-20T07:48:21.140Z", "rssi": -52 },
    { "epc": "E28011606000020C3F1A2B3C", "antennaNumber": 2, "readAtUtc": "2026-08-20T07:48:21.480Z", "rssi": -48 },
    { "epc": "E28011606000020C3F1A2B3C", "antennaNumber": 3, "readAtUtc": "2026-08-20T07:48:21.910Z", "rssi": -44 }
  ],
  "telemetry": { "firmwareVersion": "2.4.1", "queuedReads": 0 }
}
```

Response:

```json
{ "received": 3, "accepted": 3, "rejected": 0, "duplicate": false, "warnings": [], "queueDepth": 12 }
```

### Rules the sender must follow

| Rule | Why |
|---|---|
| Send **every** antenna hit, not a deduplicated summary | The server derives direction from the antenna sequence; pre-filtering destroys it |
| Include `readAtUtc` from the reader clock | Preserves ordering when a batch is delayed |
| Include a `batchId` and reuse it when retrying | Makes the call idempotent, so a timeout-and-retry cannot double-count an arrival |
| Keep batches under 500 reads | Larger batches are rejected to bound request size |
| Back off when `queueDepth` climbs | The server is falling behind; flooding it makes that worse |

`200` means safely received. Retry `5xx` with the **same** `batchId`; do not retry `4xx`.

### Heartbeats

`POST /api/v1/rfid/heartbeat` every 60 seconds.

```json
{ "deviceId": "MAIN-GATE-R01", "firmwareVersion": "2.4.1", "ipAddress": "10.0.4.21" }
```

Silence is what marks a reader offline. A quiet corridor reader with no traffic at 3pm is
still working, and the heartbeat is how it says so. Miss three intervals and the dashboard
raises an alert.

---

## 4. How direction is decided

A UHF reader does not report "a person walked through". It reports the same tag 20–50
times per second for as long as it is in the field. Turning that into one movement is the
core of the engine.

### Step 1 — group into a pass-through

Reads are buffered per *(reader, tag)*. The pass-through is closed when either:

- the tag has not been seen for the **quiet window** (default 4s), or
- the sequence has run longer than the **maximum span** (default 30s) — someone loitering
  in the field, where waiting longer would delay the event indefinitely.

### Step 2 — collapse repeats

`1,1,1,2,2,3,3,3` becomes `1,2,3`. Consecutive repeats on the same antenna carry no
directional information.

### Step 3 — resolve the direction

Four strategies, configured per reader:

| Strategy | Rule | Confidence | Use when |
|---|---|---|---|
| **AntennaRole** | Each port declared Outside or Inside; the transition decides | 1.0 | **Recommended.** Survives rewiring and renumbering |
| **AntennaOrder** | Ports wired outermost→innermost, so a rising path is entry | 0.95 clean sweep / 0.75 wandering | Roles not yet configured |
| **Fixed** | Reader can only ever mean one direction | 1.0 | A dedicated one-way lane |
| **PresenceToggle** | Inferred from the person's last known state | 0.6 | Single-antenna reader with no directional information |

**Ambiguous passes produce no event.** If the tag was seen on only one antenna, or the path
starts and ends on the same antenna (someone approached the door and turned back), nothing
is recorded. Guessing here would put a false arrival in a parent's timeline.

### Step 4 — classify and act

```
resolved movement
      ↓
resolve EPC → RfidTag → student       (unknown or revoked cards are recorded, not dropped)
      ↓
resolve reader → location
      ↓
duplicate suppression                 (same tag/location/direction within the debounce window)
      ↓
classify:  boundary location → SchoolEntry / SchoolExit
           classroom location → ClassroomEntry / ClassroomExit
           other → ZoneEntry / ZoneExit
      ↓
match against the timetable → subject, teacher, lesson slot
      ↓
persist RfidEvent
      ↓
attendance engine · guardian notification · live dashboard
```

---

## 5. Wiring a gate

A three-antenna gate, wired outermost to innermost:

```
   street side                    courtyard side
       │                                │
    [ANT 1] ────── [ANT 2] ────── [ANT 3]
     Outside      Threshold        Inside

  walking in:  1 → 2 → 3   = ENTRY
  walking out: 3 → 2 → 1   = EXIT
```

Classrooms typically use two antennas (corridor / inside). Declare the roles in
Admin → RFID → Readers so direction does not depend on the port numbering.

---

## 6. Tuning

Adjustable per reader, or system-wide in Admin → Settings:

| Setting | Default | Raise it when | Lower it when |
|---|---|---|---|
| Quiet window | 4000ms | Movements are being split in two | Events feel slow to appear |
| Maximum span | 30000ms | — | People loiter and events are delayed |
| Debounce | 60s | The same arrival is recorded twice | Legitimate re-entries are being swallowed |
| Minimum RSSI | -70 dBm | Tags in a nearby corridor are being read | Genuine passes are being missed |

**Minimum RSSI is the setting most often wrong on a new site.** A UHF reader can see a card
in a bag two rooms away. If students appear to enter rooms they never visited, raise it
(toward -50) until only people actually crossing the threshold are read.

---

## 7. Testing without hardware

The simulator injects reads through the *same* queue as a physical reader, so notifications
and attendance behave exactly as they will on site.

Admin portal → Live monitor → **Simulate a pass**, or:

```bash
curl -X POST http://localhost:5080/api/v1/rfid/simulate \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"MAIN-GATE-R01","epc":"E28011606000020C3F1A2B3C","direction":"Entry","readsPerAntenna":5}'
```

`POST /api/v1/rfid/simulate/school-day?studentId=1` replays a full plausible day against
the student's timetable — useful for demonstrating the parent app before readers are mounted.

Simulation sits behind its own `rfid.simulate` permission, separate from ordinary admin
access, so synthetic attendance cannot be created casually.

---

## 8. Failure handling

| Failure | Behaviour |
|---|---|
| Reader loses power | Marked offline after three missed heartbeats; dashboard alert raised |
| Internet drops | Gateway buffers and forwards on reconnect; `readAtUtc` preserves the real times |
| Server restarts mid-pass | Buffered pass-throughs are flushed on shutdown |
| Unknown card | Recorded as `UnknownTag` with the reason — usually a visitor or an unassigned card |
| Revoked card | Recorded as `Rejected`; no attendance is produced |
| Processing fails | Retried three times with backoff, then dead-lettered for replay — never dropped silently |
| Duplicate batch | Detected by `batchId` and ignored |
| Device clock wrong | Skew beyond the configured limit is clamped to server time and flagged |
| Missed exit read | The next entry closes the stale interval; end-of-day sweep closes the rest |

Dead letters are visible in Admin → RFID with the original payload and the error.

---

## 9. Diagnostics

| Endpoint | Shows |
|---|---|
| `GET /api/v1/rfid/pipeline` | Queue depth, throughput, dropped reads, in-flight passes |
| `GET /api/v1/rfid/readers` | Every reader's status, last heartbeat, events today |
| `GET /api/v1/rfid/readers/{id}/logs` | Connects, disconnects and errors for one device |
| `GET /api/v1/rfid/events?includeRejected=true` | Movements including unknown cards |
| `GET /api/v1/rfid/whoami` | Lets a newly provisioned device confirm its key works |

A non-zero `totalDropped` means the processor could not keep up and reads were lost — the
one number worth alerting on.

---

## 10. Adding a different reader model

Implement `IReaderHardwareAdapter` (in `CampusTrack.Application/Rfid`). The adapter's only
job is to turn whatever the device emits into `RfidReadItem` and hand it to the ingestion
API. It must not interpret direction, resolve students or decide attendance: those are
server decisions made with context the device does not have.

Everything above that interface — direction resolution, attendance, notifications — is
hardware-agnostic and fully covered by tests, so supporting a new model is one adapter
rather than a change to the engine.
