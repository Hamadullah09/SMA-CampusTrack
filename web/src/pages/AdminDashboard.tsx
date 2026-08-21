import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer,
  Tooltip, XAxis, YAxis,
} from 'recharts';
import { api, describeError } from '@/api/client';
import { useRealtimeEvent } from '@/lib/realtime';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, Stat,
} from '@/components/ui';
import './dashboard.css';

interface AdminDashboardData {
  totalStudents: number;
  totalTeachers: number;
  totalGuardians: number;
  totalStaff: number;
  studentsOnCampus: number;
  studentsOffsite: number;
  studentsInRooms: number;
  presentToday: number;
  absentToday: number;
  lateToday: number;
  attendanceRateToday: number;
  readersTotal: number;
  readersOnline: number;
  readersOffline: number;
  unassignedCards: number;
  eventsToday: number;
  unknownTagReadsToday: number;
  pendingDeadLetters: number;
  pendingGuardianLinks: number;
  recentEvents: RfidEvent[];
  readers: ReaderStatus[];
  attendanceTrend: TrendPoint[];
  arrivalFlow: FlowPoint[];
  alerts: DashboardAlert[];
}

interface RfidEvent {
  id: number;
  eventTypeName: string;
  studentName?: string;
  studentCode?: string;
  locationName?: string;
  occurredAtUtc: string;
  confidence: number;
}

interface ReaderStatus {
  id: number;
  deviceId: string;
  name: string;
  statusName: string;
  locationName: string;
  secondsSinceHeartbeat?: number;
  eventsToday: number;
}

interface TrendPoint { date: string; present: number; absent: number; late: number; percentage: number; }
interface FlowPoint { hour: number; entries: number; exits: number; }
interface DashboardAlert { severity: string; title: string; message: string; link?: string; }

export function AdminDashboard() {
  const queryClient = useQueryClient();
  const [liveEvents, setLiveEvents] = useState<RfidEvent[]>([]);

  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ['dashboard', 'admin'],
    queryFn: async () => (await api.get<AdminDashboardData>('/dashboard/admin')).data,
    // A safety net: the live feed is primary, but a missed socket message should not leave
    // the numbers stale for the rest of the day.
    refetchInterval: 120_000,
  });

  // New movements are prepended locally so the feed updates instantly, rather than waiting
  // for a refetch that would also disturb the rest of the page.
  useRealtimeEvent<RfidEvent & { eventType?: string }>('rfidEvent', useCallback((payload) => {
    setLiveEvents((current) => [
      { ...payload, eventTypeName: payload.eventTypeName ?? payload.eventType ?? 'Movement' },
      ...current,
    ].slice(0, 12));

    // Counters do move with each event, so nudge them without blocking the feed.
    queryClient.invalidateQueries({ queryKey: ['dashboard', 'admin'], refetchType: 'none' });
  }, [queryClient]));

  if (isLoading) {
    return (
      <>
        <PageHeader />
        <div className="stat-grid">
          {Array.from({ length: 4 }, (_, i) => <Stat key={i} label="" value="" loading />)}
        </div>
      </>
    );
  }

  if (isError || !data) {
    return (
      <>
        <PageHeader />
        <Card>
          <ErrorState
            title="Could not load the dashboard"
            message={describeError(error)}
            onRetry={() => void refetch()}
          />
        </Card>
      </>
    );
  }

  const events = [...liveEvents, ...data.recentEvents].slice(0, 12);
  const attendanceTone =
    data.attendanceRateToday >= 90 ? 'success' : data.attendanceRateToday >= 75 ? 'warning' : 'danger';

  return (
    <>
      <PageHeader
        actions={
          <Button icon="refresh" onClick={() => void refetch()} loading={isFetching}>
            Refresh
          </Button>
        }
      />

      {data.alerts.length > 0 && (
        <div className="dash-alerts">
          {data.alerts.map((alert, index) => (
            <div key={index} className={`alert alert-${toneOf(alert.severity)}`}>
              <Icon name={alert.severity === 'error' ? 'alert' : 'info'} />
              <div className="grow">
                <div className="alert-title">{alert.title}</div>
                <div className="alert-body">{alert.message}</div>
              </div>
              {alert.link && (
                <Link to={alert.link} className="btn btn-secondary btn-sm">Review</Link>
              )}
            </div>
          ))}
        </div>
      )}

      <div className="stat-grid">
        <Stat
          label="On campus now"
          value={data.studentsOnCampus}
          meta={`${data.studentsInRooms} in monitored rooms`}
          icon="users"
          accent="success"
        />
        <Stat
          label="Attendance today"
          value={`${data.attendanceRateToday}%`}
          meta={`${data.presentToday} present · ${data.absentToday} absent · ${data.lateToday} late`}
          icon="check"
          accent={attendanceTone}
        />
        <Stat
          label="Readers online"
          value={`${data.readersOnline}/${data.readersTotal}`}
          meta={data.readersOffline > 0 ? `${data.readersOffline} offline` : 'All reporting'}
          icon="rfid"
          accent={data.readersOffline > 0 ? 'danger' : 'success'}
        />
        <Stat
          label="Movements today"
          value={data.eventsToday.toLocaleString()}
          meta={data.unknownTagReadsToday > 0 ? `${data.unknownTagReadsToday} unrecognised cards` : 'All cards recognised'}
          icon="activity"
          accent="info"
        />
      </div>

      <div className="dash-grid">
        <Card
          title="Attendance over the last two weeks"
          subtitle="Daily present, late and absent counts"
          className="dash-span-2"
        >
          {data.attendanceTrend.length === 0 ? (
            <EmptyState
              title="No attendance recorded yet"
              message="Once students start arriving, their attendance appears here."
              icon="chart"
            />
          ) : (
            <ResponsiveContainer width="100%" height={260}>
              <AreaChart data={data.attendanceTrend} margin={{ top: 8, right: 8, left: -18, bottom: 0 }}>
                <defs>
                  <linearGradient id="presentFill" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="var(--success-solid)" stopOpacity={0.34} />
                    <stop offset="100%" stopColor="var(--success-solid)" stopOpacity={0.02} />
                  </linearGradient>
                  <linearGradient id="absentFill" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="var(--danger-solid)" stopOpacity={0.28} />
                    <stop offset="100%" stopColor="var(--danger-solid)" stopOpacity={0.02} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border-subtle)" vertical={false} />
                <XAxis
                  dataKey="date" tickFormatter={shortDate} stroke="var(--text-muted)"
                  fontSize={11} tickLine={false} axisLine={false}
                />
                <YAxis stroke="var(--text-muted)" fontSize={11} tickLine={false} axisLine={false} />
                <Tooltip content={<ChartTooltip labelFormatter={longDate} />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Area
                  type="monotone" dataKey="present" name="Present" stroke="var(--success-solid)"
                  fill="url(#presentFill)" strokeWidth={2}
                />
                <Area
                  type="monotone" dataKey="absent" name="Absent" stroke="var(--danger-solid)"
                  fill="url(#absentFill)" strokeWidth={2}
                />
              </AreaChart>
            </ResponsiveContainer>
          )}
        </Card>

        <Card
          title="Live movement"
          subtitle="Gate and classroom activity as it happens"
          actions={<Badge tone="success" live>Live</Badge>}
          flush
        >
          {events.length === 0 ? (
            <EmptyState
              title="No movement yet today"
              message="Events appear here the moment a card is read at any gate or door."
              icon="activity"
            />
          ) : (
            <ul className="event-feed">
              {events.map((event, index) => (
                <li key={`${event.id}-${index}`} className={index < liveEvents.length ? 'is-new' : ''}>
                  <span className={`event-dot event-${directionOf(event.eventTypeName)}`}>
                    <Icon name={directionOf(event.eventTypeName) === 'in' ? 'login' : 'logout'} size={13} />
                  </span>
                  <div className="event-body">
                    <strong>{event.studentName ?? 'Unrecognised card'}</strong>
                    <span>{describeEvent(event.eventTypeName)} · {event.locationName}</span>
                  </div>
                  <time className="event-time tabular">{timeOf(event.occurredAtUtc)}</time>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title="Arrivals and departures by hour" subtitle="Today's flow through the gates">
          {data.arrivalFlow.length === 0 ? (
            <EmptyState title="No gate activity yet" message="The morning rush will appear here." icon="chart" />
          ) : (
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={data.arrivalFlow} margin={{ top: 8, right: 8, left: -18, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border-subtle)" vertical={false} />
                <XAxis
                  dataKey="hour" tickFormatter={(h: number) => `${h}:00`}
                  stroke="var(--text-muted)" fontSize={11} tickLine={false} axisLine={false}
                />
                <YAxis stroke="var(--text-muted)" fontSize={11} tickLine={false} axisLine={false} />
                <Tooltip content={<ChartTooltip labelFormatter={(h) => `${h}:00`} />} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Bar dataKey="entries" name="Arrivals" fill="var(--brand-500)" radius={[4, 4, 0, 0]} />
                <Bar dataKey="exits" name="Departures" fill="var(--slate-400)" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </Card>

        <Card
          title="Reader status"
          subtitle={`${data.readersOnline} of ${data.readersTotal} reporting`}
          actions={<Link to="/rfid/readers" className="btn btn-secondary btn-sm">Manage</Link>}
          flush
        >
          {data.readers.length === 0 ? (
            <EmptyState
              title="No readers configured"
              message="Add your first RFID reader to begin tracking movement."
              icon="rfid"
              action={<Link to="/rfid/readers" className="btn btn-primary btn-sm">Add a reader</Link>}
            />
          ) : (
            <ul className="reader-list">
              {data.readers.slice(0, 6).map((reader) => (
                <li key={reader.id}>
                  <span className={`reader-status reader-${reader.statusName.toLowerCase()}`} />
                  <div className="event-body">
                    <strong>{reader.name}</strong>
                    <span>{reader.locationName} · {reader.eventsToday} today</span>
                  </div>
                  <Badge tone={reader.statusName === 'Online' ? 'success' : 'danger'}>
                    {reader.statusName}
                  </Badge>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <div className="stat-grid" style={{ marginTop: 'var(--space-5)' }}>
        <Stat label="Students" value={data.totalStudents} icon="users" />
        <Stat label="Teachers" value={data.totalTeachers} icon="teacher" />
        <Stat label="Parents" value={data.totalGuardians} icon="user" />
        <Stat
          label="Unassigned cards"
          value={data.unassignedCards}
          icon="shield"
          accent={data.unassignedCards > 0 ? 'warning' : 'brand'}
        />
      </div>
    </>
  );
}

function PageHeader({ actions }: { actions?: React.ReactNode }) {
  return (
    <div className="page-header">
      <div>
        <h1 className="page-title">School overview</h1>
        <p className="page-subtitle">Live attendance, movement and system health</p>
      </div>
      {actions}
    </div>
  );
}

/** A tooltip that inherits the theme, rather than Recharts' default white box. */
function ChartTooltip({ active, payload, label, labelFormatter }: {
  active?: boolean;
  payload?: Array<{ name?: string; value?: number | string; color?: string }>;
  label?: string | number;
  labelFormatter?: (value: never) => string;
}) {
  if (!active || !payload?.length) return null;

  return (
    <div className="chart-tooltip">
      <div className="chart-tooltip-label">
        {labelFormatter ? labelFormatter(label as never) : String(label)}
      </div>
      {payload.map((entry, index) => (
        <div key={index} className="chart-tooltip-row">
          <span className="chart-tooltip-swatch" style={{ background: entry.color }} />
          <span>{entry.name}</span>
          <strong className="tabular">{entry.value}</strong>
        </div>
      ))}
    </div>
  );
}

function toneOf(severity: string) {
  return severity === 'error' ? 'error' : severity === 'warning' ? 'warning' : 'info';
}

function directionOf(eventType: string): 'in' | 'out' {
  return eventType.includes('Entry') ? 'in' : 'out';
}

function describeEvent(eventType: string) {
  switch (eventType) {
    case 'SchoolEntry': return 'Arrived at school';
    case 'SchoolExit': return 'Left school';
    case 'ClassroomEntry': return 'Entered classroom';
    case 'ClassroomExit': return 'Left classroom';
    case 'ZoneEntry': return 'Entered';
    case 'ZoneExit': return 'Left';
    case 'UnknownTag': return 'Unrecognised card';
    default: return eventType;
  }
}

function timeOf(iso: string) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function shortDate(value: string) {
  return new Date(value).toLocaleDateString([], { day: 'numeric', month: 'short' });
}

function longDate(value: string) {
  return new Date(value).toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' });
}
