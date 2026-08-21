import { useCallback, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import { useRealtime, useRealtimeEvent } from '@/lib/realtime';
import { P, useAuth } from '@/lib/auth';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Modal, Stat, useToast,
} from '@/components/ui';
import { describeError as describe } from '@/api/client';
import './monitor.css';

interface ReaderStatus {
  id: number;
  deviceId: string;
  name: string;
  model: string;
  statusName: string;
  locationName: string;
  locationType: string;
  ipAddress?: string;
  firmwareVersion?: string;
  lastHeartbeatUtc?: string;
  lastEventUtc?: string;
  secondsSinceHeartbeat?: number;
  lastErrorMessage?: string;
  antennaCount: number;
  eventsToday: number;
  mapX?: number;
  mapY?: number;
}

interface LiveEvent {
  id?: number;
  eventType?: string;
  eventTypeName?: string;
  studentName?: string;
  studentCode?: string;
  locationName?: string;
  occurredAtUtc: string;
  confidence?: number;
  rejectionReason?: string;
}

interface Presence {
  onCampus: number;
  offsite: number;
  inRooms: number;
  totalActiveStudents: number;
}

/**
 * The live operations view.
 *
 * Two things a school actually needs from an RFID system, on one screen: is the hardware
 * working, and who is moving right now. The floor plan places readers where they physically
 * are, so an offline device is identified by location rather than by device id.
 */
export function RfidMonitorPage() {
  const { can } = useAuth();
  const toast = useToast();
  const { connected } = useRealtime();

  const [events, setEvents] = useState<LiveEvent[]>([]);
  const [selectedReader, setSelectedReader] = useState<ReaderStatus | null>(null);
  const [simulateOpen, setSimulateOpen] = useState(false);

  const readersQuery = useQuery({
    queryKey: ['rfid', 'readers'],
    queryFn: async () => (await api.get<ReaderStatus[]>('/rfid/readers')).data,
    refetchInterval: 30_000,
  });

  const presenceQuery = useQuery({
    queryKey: ['rfid', 'presence'],
    queryFn: async () => (await api.get<Presence>('/rfid/presence')).data,
    refetchInterval: 30_000,
  });

  const recentQuery = useQuery({
    queryKey: ['rfid', 'recent'],
    queryFn: async () => (await api.get<LiveEvent[]>('/rfid/events/recent?count=30')).data,
  });

  useRealtimeEvent<LiveEvent>('rfidEvent', useCallback((payload) => {
    setEvents((current) => [payload, ...current].slice(0, 40));
  }, []));

  useRealtimeEvent<{ id: number; status: string }>('readerStatus', useCallback(() => {
    void readersQuery.refetch();
  }, [readersQuery]));

  const readers = readersQuery.data ?? [];
  const feed = useMemo(
    () => [...events, ...(recentQuery.data ?? [])].slice(0, 40),
    [events, recentQuery.data],
  );

  const offline = readers.filter((r) => r.statusName === 'Offline');
  // Readers with map coordinates are drawn on the plan; the rest are listed beneath it.
  const placed = readers.filter((r) => r.mapX != null && r.mapY != null);
  const unplaced = readers.filter((r) => r.mapX == null || r.mapY == null);

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Live monitor</h1>
          <p className="page-subtitle">Reader health and movement across the school</p>
        </div>
        <div className="row">
          <Badge tone={connected ? 'success' : 'warning'} live={connected}>
            {connected ? 'Live feed connected' : 'Reconnecting…'}
          </Badge>
          {can(P.rfidSimulate) && (
            <Button icon="activity" onClick={() => setSimulateOpen(true)}>Simulate a pass</Button>
          )}
        </div>
      </div>

      <div className="stat-grid">
        <Stat
          label="On campus" value={presenceQuery.data?.onCampus ?? 0}
          meta={`of ${presenceQuery.data?.totalActiveStudents ?? 0} students`}
          icon="users" accent="success" loading={presenceQuery.isLoading}
        />
        <Stat
          label="In monitored rooms" value={presenceQuery.data?.inRooms ?? 0}
          icon="door" accent="info" loading={presenceQuery.isLoading}
        />
        <Stat
          label="Readers online" value={`${readers.length - offline.length}/${readers.length}`}
          meta={offline.length > 0 ? `${offline.length} need attention` : 'All reporting'}
          icon="rfid" accent={offline.length > 0 ? 'danger' : 'success'}
          loading={readersQuery.isLoading}
        />
        <Stat
          label="Events today"
          value={readers.reduce((total, r) => total + r.eventsToday, 0).toLocaleString()}
          icon="activity" loading={readersQuery.isLoading}
        />
      </div>

      {offline.length > 0 && (
        <div className="alert alert-error" style={{ marginTop: 'var(--space-5)' }}>
          <Icon name="alert" />
          <div className="grow">
            <div className="alert-title">
              {offline.length} reader{offline.length === 1 ? '' : 's'} not responding
            </div>
            <div className="alert-body">
              Movement is not being recorded at: {offline.map((r) => r.locationName).join(', ')}.
              Check power and network at these locations.
            </div>
          </div>
        </div>
      )}

      <div className="monitor-grid">
        <Card
          title="School floor plan"
          subtitle="Select a reader to see its recent activity"
          className="monitor-map-card"
        >
          {readersQuery.isLoading ? (
            <LoadingState rows={4} />
          ) : readers.length === 0 ? (
            <EmptyState
              title="No readers configured"
              message="Add readers and give them map coordinates to see them here."
              icon="rfid"
            />
          ) : (
            <>
              <div className="floor-plan">
                <div className="floor-plan-grid" aria-hidden="true" />
                {placed.map((reader) => (
                  <button
                    key={reader.id}
                    className={`plan-node plan-${reader.statusName.toLowerCase()}`}
                    style={{ left: `${reader.mapX! * 100}%`, top: `${reader.mapY! * 100}%` }}
                    onClick={() => setSelectedReader(reader)}
                    title={`${reader.name} — ${reader.locationName} (${reader.statusName})`}
                    aria-label={`${reader.name} at ${reader.locationName}, ${reader.statusName}`}
                  >
                    <Icon name={iconForLocation(reader.locationType)} size={14} />
                    <span className="plan-node-label">{reader.locationName}</span>
                  </button>
                ))}

                {placed.length === 0 && (
                  <p className="floor-plan-hint">
                    No readers have map coordinates yet. Set them on each RFID location to place
                    devices on this plan.
                  </p>
                )}
              </div>

              {unplaced.length > 0 && (
                <div className="unplaced">
                  <p className="muted" style={{ fontSize: 'var(--text-sm)', marginBottom: 'var(--space-2)' }}>
                    Not placed on the plan
                  </p>
                  <div className="row wrap">
                    {unplaced.map((reader) => (
                      <button
                        key={reader.id}
                        className={`chip chip-${reader.statusName.toLowerCase()}`}
                        onClick={() => setSelectedReader(reader)}
                      >
                        <span className="chip-dot" />
                        {reader.name}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}
        </Card>

        <Card
          title="Movement feed"
          subtitle="Newest first"
          actions={<Badge tone={connected ? 'success' : 'neutral'} live={connected}>Live</Badge>}
          flush
        >
          {recentQuery.isLoading ? (
            <LoadingState rows={6} />
          ) : recentQuery.isError ? (
            <ErrorState message={describeError(recentQuery.error)} onRetry={() => void recentQuery.refetch()} />
          ) : feed.length === 0 ? (
            <EmptyState
              title="Nothing yet today"
              message="Movement appears the instant a card is read at any monitored door."
              icon="activity"
            />
          ) : (
            <ul className="event-feed monitor-feed">
              {feed.map((event, index) => {
                const type = event.eventTypeName ?? event.eventType ?? '';
                const rejected = type === 'UnknownTag' || type === 'Rejected';

                return (
                  <li key={`${event.id ?? 'live'}-${index}`} className={index < events.length ? 'is-new' : ''}>
                    <span className={`event-dot ${rejected ? 'event-rejected' : `event-${type.includes('Entry') ? 'in' : 'out'}`}`}>
                      <Icon
                        name={rejected ? 'alert' : type.includes('Entry') ? 'login' : 'logout'}
                        size={13}
                      />
                    </span>
                    <div className="event-body">
                      <strong>{event.studentName ?? 'Unrecognised card'}</strong>
                      <span>
                        {rejected
                          ? event.rejectionReason ?? 'Card not recognised'
                          : `${humanEvent(type)} · ${event.locationName ?? ''}`}
                      </span>
                    </div>
                    <time className="event-time tabular">
                      {new Date(event.occurredAtUtc).toLocaleTimeString([], {
                        hour: '2-digit', minute: '2-digit', second: '2-digit',
                      })}
                    </time>
                  </li>
                );
              })}
            </ul>
          )}
        </Card>
      </div>

      <ReaderDetailModal reader={selectedReader} onClose={() => setSelectedReader(null)} />

      <SimulateModal
        open={simulateOpen}
        readers={readers}
        onClose={() => setSimulateOpen(false)}
        onDone={(message) => {
          toast.success('Simulation queued', message);
          setSimulateOpen(false);
        }}
        onError={(message) => toast.error('Could not simulate', message)}
      />
    </>
  );
}

function ReaderDetailModal({ reader, onClose }: { reader: ReaderStatus | null; onClose: () => void }) {
  const query = useQuery({
    queryKey: ['rfid', 'reader-events', reader?.id],
    queryFn: async () =>
      (await api.get<{ items: LiveEvent[] }>(`/rfid/readers/${reader!.id}/events?pageSize=10`)).data,
    enabled: reader !== null,
  });

  if (!reader) return null;

  return (
    <Modal open onClose={onClose} title={reader.name} size="lg">
      <div className="detail-grid">
        <Detail label="Location" value={reader.locationName} />
        <Detail
          label="Status"
          value={
            <Badge tone={reader.statusName === 'Online' ? 'success' : 'danger'} dot>
              {reader.statusName}
            </Badge>
          }
        />
        <Detail label="Device id" value={<code className="mono">{reader.deviceId}</code>} />
        <Detail label="Model" value={reader.model} />
        <Detail label="IP address" value={reader.ipAddress ?? '—'} />
        <Detail label="Firmware" value={reader.firmwareVersion ?? '—'} />
        <Detail label="Antennas" value={reader.antennaCount} />
        <Detail label="Events today" value={reader.eventsToday.toLocaleString()} />
        <Detail
          label="Last heartbeat"
          value={
            reader.secondsSinceHeartbeat == null
              ? 'Never'
              : `${formatAge(reader.secondsSinceHeartbeat)} ago`
          }
        />
        <Detail
          label="Last event"
          value={reader.lastEventUtc ? new Date(reader.lastEventUtc).toLocaleString() : 'None'}
        />
      </div>

      {reader.lastErrorMessage && (
        <div className="alert alert-error" style={{ marginTop: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">Last reported problem</div>
            <div className="alert-body">{reader.lastErrorMessage}</div>
          </div>
        </div>
      )}

      <h3 style={{ margin: 'var(--space-5) 0 var(--space-2)', fontSize: 'var(--text-md)' }}>
        Recent activity
      </h3>

      {query.isLoading ? (
        <LoadingState rows={3} />
      ) : !query.data?.items.length ? (
        <EmptyState title="No events from this reader yet" icon="activity" />
      ) : (
        <ul className="event-feed" style={{ maxHeight: 220 }}>
          {query.data.items.map((event, index) => (
            <li key={index} style={{ paddingLeft: 0, paddingRight: 0 }}>
              <div className="event-body">
                <strong>{event.studentName ?? 'Unrecognised card'}</strong>
                <span>{humanEvent(event.eventTypeName ?? '')}</span>
              </div>
              <time className="event-time tabular">
                {new Date(event.occurredAtUtc).toLocaleTimeString()}
              </time>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}

function SimulateModal({
  open, readers, onClose, onDone, onError,
}: {
  open: boolean;
  readers: ReaderStatus[];
  onClose: () => void;
  onDone: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [deviceId, setDeviceId] = useState('');
  const [epc, setEpc] = useState('');
  const [direction, setDirection] = useState('Entry');
  const [busy, setBusy] = useState(false);

  async function run() {
    setBusy(true);
    try {
      const { data } = await api.post<{ message: string }>('/rfid/simulate', {
        deviceId, epc, direction, readsPerAntenna: 5,
      });
      onDone(data.message);
    } catch (error) {
      onError(describe(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Simulate a card pass"
      footer={
        <>
          <Button onClick={onClose} disabled={busy}>Cancel</Button>
          <Button variant="primary" loading={busy} disabled={!deviceId || !epc} onClick={run}>
            Queue the pass
          </Button>
        </>
      }
    >
      <p className="muted" style={{ marginBottom: 'var(--space-4)' }}>
        Injects reads through exactly the same pipeline as a physical reader, so notifications
        and attendance behave as they would on site. Useful before the hardware is mounted.
      </p>

      <div className="stack">
        <div className="field">
          <label className="label label-required" htmlFor="sim-reader">Reader</label>
          <select
            id="sim-reader" className="select" value={deviceId}
            onChange={(e) => setDeviceId(e.target.value)}
          >
            <option value="">Choose a reader</option>
            {readers.map((reader) => (
              <option key={reader.id} value={reader.deviceId}>
                {reader.name} — {reader.locationName}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label className="label label-required" htmlFor="sim-epc">Card EPC</label>
          <input
            id="sim-epc" className="input mono" value={epc}
            placeholder="E28011606000020C3F1A2B3C"
            onChange={(e) => setEpc(e.target.value.toUpperCase())}
          />
          <span className="field-hint">The EPC of a card already assigned to a student.</span>
        </div>

        <div className="field">
          <label className="label" htmlFor="sim-direction">Direction</label>
          <select
            id="sim-direction" className="select" value={direction}
            onChange={(e) => setDirection(e.target.value)}
          >
            <option value="Entry">Entry — walking in</option>
            <option value="Exit">Exit — walking out</option>
          </select>
        </div>
      </div>
    </Modal>
  );
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="detail">
      <span className="detail-label">{label}</span>
      <span className="detail-value">{value}</span>
    </div>
  );
}

function iconForLocation(type: string) {
  if (type === 'MainGate' || type === 'ExitGate') return 'login' as const;
  if (type === 'Library') return 'book' as const;
  if (type === 'Cafeteria') return 'inbox' as const;
  return 'door' as const;
}

function humanEvent(type: string) {
  switch (type) {
    case 'SchoolEntry': return 'Arrived at school';
    case 'SchoolExit': return 'Left school';
    case 'ClassroomEntry': return 'Entered classroom';
    case 'ClassroomExit': return 'Left classroom';
    case 'ZoneEntry': return 'Entered area';
    case 'ZoneExit': return 'Left area';
    default: return type || 'Movement';
  }
}

function formatAge(seconds: number) {
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  return `${Math.floor(seconds / 86400)}d`;
}
