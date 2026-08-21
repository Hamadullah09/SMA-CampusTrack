import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { api, describeError } from '@/api/client';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState,
} from '@/components/ui';

interface ReportDefinition {
  id: string;
  title: string;
  description: string;
  path: string;
  icon: 'check' | 'activity' | 'chart' | 'clock' | 'rfid';
  supportsDates: boolean;
  supportsSection: boolean;
}

/**
 * Reporting.
 *
 * Every report renders on screen and exports in the same three formats from the same data,
 * so what a head teacher shows a governor is exactly what they saw here.
 */
const REPORTS: ReportDefinition[] = [
  {
    id: 'attendance',
    title: 'Attendance summary',
    description: 'Attendance percentage per student, and who is below the requirement.',
    path: '/reports/attendance',
    icon: 'check',
    supportsDates: true,
    supportsSection: true,
  },
  {
    id: 'late-arrivals',
    title: 'Late arrivals',
    description: 'Everyone who arrived after the late threshold, and by how long.',
    path: '/reports/late-arrivals',
    icon: 'clock',
    supportsDates: true,
    supportsSection: false,
  },
  {
    id: 'rfid-movements',
    title: 'Movement log',
    description: 'Every gate and room movement, with the reader that recorded it.',
    path: '/reports/rfid-movements',
    icon: 'activity',
    supportsDates: true,
    supportsSection: false,
  },
  {
    id: 'reader-activity',
    title: 'Reader activity',
    description: 'Uptime, throughput and errors for each RFID reader.',
    path: '/reports/reader-activity',
    icon: 'rfid',
    supportsDates: true,
    supportsSection: false,
  },
  {
    id: 'academic',
    title: 'Academic performance',
    description: 'Average, best and lowest marks per student and subject.',
    path: '/reports/academic',
    icon: 'chart',
    supportsDates: false,
    supportsSection: true,
  },
];

export function ReportsPage() {
  const [selected, setSelected] = useState<ReportDefinition>(REPORTS[0]);
  const [from, setFrom] = useState(() => isoDate(-30));
  const [to, setTo] = useState(() => isoDate(0));
  const [sectionId, setSectionId] = useState('');

  const sections = useQuery({
    queryKey: ['sections'],
    queryFn: async () =>
      (await api.get<Array<{ id: number; displayName: string }>>('/academics/sections')).data,
  });

  const params = {
    from: selected.supportsDates ? from : undefined,
    to: selected.supportsDates ? to : undefined,
    sectionId: selected.supportsSection && sectionId ? sectionId : undefined,
  };

  const report = useQuery({
    queryKey: ['report', selected.id, params],
    queryFn: async () => {
      const { data } = await api.get<{ count: number; title: string; items: Array<Record<string, unknown>> }>(
        selected.path, { params },
      );
      return data;
    },
  });

  const query = new URLSearchParams(
    Object.entries(params).filter(([, v]) => v != null) as [string, string][],
  ).toString();

  const exportUrl = (format: string) =>
    `/api/v1${selected.path}?format=${format}${query ? `&${query}` : ''}`;

  const rows = report.data?.items ?? [];
  const columns = rows.length > 0 ? Object.keys(rows[0]) : [];

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Reports</h1>
          <p className="page-subtitle">View on screen, then export the same data</p>
        </div>
      </div>

      <div className="report-layout">
        <div className="report-picker">
          {REPORTS.map((definition) => (
            <button
              key={definition.id}
              className={`report-option ${selected.id === definition.id ? 'is-selected' : ''}`}
              onClick={() => setSelected(definition)}
            >
              <span className="report-icon"><Icon name={definition.icon} size={16} /></span>
              <span className="report-text">
                <strong>{definition.title}</strong>
                <span>{definition.description}</span>
              </span>
            </button>
          ))}
        </div>

        <div className="stack">
          <Card
            title={selected.title}
            subtitle={selected.description}
            actions={
              <div className="row">
                {/* All three formats, always: a spreadsheet for analysis, a PDF to print,
                    CSV for whatever the local authority asks for. */}
                <a className="btn btn-secondary btn-sm" href={exportUrl('csv')} target="_blank" rel="noreferrer">CSV</a>
                <a className="btn btn-secondary btn-sm" href={exportUrl('xlsx')} target="_blank" rel="noreferrer">Excel</a>
                <a className="btn btn-secondary btn-sm" href={exportUrl('pdf')} target="_blank" rel="noreferrer">PDF</a>
              </div>
            }
          >
            <div className="row wrap">
              {selected.supportsDates && (
                <>
                  <div className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 'var(--space-2)' }}>
                    <label className="label" htmlFor="rep-from" style={{ margin: 0 }}>From</label>
                    <input id="rep-from" className="input" type="date" style={{ width: 'auto' }}
                      value={from} onChange={(e) => setFrom(e.target.value)} />
                  </div>
                  <div className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 'var(--space-2)' }}>
                    <label className="label" htmlFor="rep-to" style={{ margin: 0 }}>To</label>
                    <input id="rep-to" className="input" type="date" style={{ width: 'auto' }}
                      value={to} onChange={(e) => setTo(e.target.value)} />
                  </div>
                </>
              )}

              {selected.supportsSection && (
                <select className="select" style={{ width: 'auto' }} value={sectionId}
                  onChange={(e) => setSectionId(e.target.value)} aria-label="Filter by section">
                  <option value="">All sections</option>
                  {sections.data?.map((section) => (
                    <option key={section.id} value={section.id}>{section.displayName}</option>
                  ))}
                </select>
              )}

              <div className="grow" />

              <Button size="sm" icon="refresh" aria-label="Refresh"
                onClick={() => void report.refetch()} loading={report.isFetching} />
            </div>
          </Card>

          {/* Attendance is the report people act on, so it gets a chart: the outliers at the
              bottom of the distribution are the point, and a table buries them. */}
          {selected.id === 'attendance' && rows.length > 0 && (
            <Card title="Lowest attendance" subtitle="The students most at risk">
              <ResponsiveContainer width="100%" height={240}>
                <BarChart
                  data={rows.slice(0, 12).map((r) => ({
                    name: shortName(String(r.studentName ?? '')),
                    value: Number(r.attendancePercentage ?? 0),
                  }))}
                  margin={{ top: 8, right: 8, left: -20, bottom: 0 }}
                >
                  <CartesianGrid strokeDasharray="3 3" stroke="var(--border-subtle)" vertical={false} />
                  <XAxis dataKey="name" stroke="var(--text-muted)" fontSize={11}
                    tickLine={false} axisLine={false} interval={0} angle={-30} textAnchor="end" height={54} />
                  <YAxis stroke="var(--text-muted)" fontSize={11} tickLine={false} axisLine={false} domain={[0, 100]} />
                  <Tooltip
                    contentStyle={{
                      background: 'var(--bg-raised)', border: '1px solid var(--border-subtle)',
                      borderRadius: 'var(--radius-md)', fontSize: 13,
                    }}
                    formatter={(value: number) => [`${value}%`, 'Attendance']}
                  />
                  <Bar dataKey="value" radius={[4, 4, 0, 0]}>
                    {rows.slice(0, 12).map((r, index) => {
                      const value = Number(r.attendancePercentage ?? 0);
                      return (
                        <Cell
                          key={index}
                          fill={value >= 90 ? 'var(--success-solid)'
                            : value >= 75 ? 'var(--warning-solid)'
                            : 'var(--danger-solid)'}
                        />
                      );
                    })}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </Card>
          )}

          <Card
            flush
            title="Results"
            subtitle={report.data ? `${report.data.count.toLocaleString()} row(s)` : undefined}
          >
            {report.isLoading ? (
              <LoadingState rows={8} />
            ) : report.isError ? (
              <ErrorState message={describeError(report.error)} onRetry={() => void report.refetch()} />
            ) : rows.length === 0 ? (
              <EmptyState
                title="Nothing to report for that range"
                message="Try widening the dates or removing the section filter."
                icon="chart"
              />
            ) : (
              <div className="table-wrap">
                <table className="table table-responsive">
                  <thead>
                    <tr>{columns.map((key) => <th key={key}>{humanise(key)}</th>)}</tr>
                  </thead>
                  <tbody>
                    {rows.slice(0, 200).map((row, index) => (
                      <tr key={index}>
                        {columns.map((key) => (
                          <td key={key} data-label={humanise(key)}>{renderCell(key, row[key])}</td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>

                {rows.length > 200 && (
                  <div className="pagination">
                    <span className="pagination-info">
                      Showing the first 200 of {rows.length.toLocaleString()}. Export to see them all.
                    </span>
                  </div>
                )}
              </div>
            )}
          </Card>
        </div>
      </div>

      <style>{`
        .report-layout {
          display: grid;
          grid-template-columns: 280px minmax(0, 1fr);
          gap: var(--space-4);
          align-items: start;
        }
        .report-picker {
          display: flex;
          flex-direction: column;
          gap: var(--space-2);
          position: sticky;
          top: calc(var(--topbar-height) + var(--space-4));
        }
        .report-option {
          display: flex;
          gap: var(--space-3);
          padding: var(--space-3);
          border-radius: var(--radius-md);
          border: 1px solid var(--border-subtle);
          background: var(--bg-surface);
          text-align: left;
          transition: all var(--duration-fast) var(--ease-out);
        }
        .report-option:hover { border-color: var(--border-strong); background: var(--bg-hover); }
        .report-option.is-selected {
          border-color: var(--brand-500);
          background: var(--brand-50);
        }
        [data-theme='dark'] .report-option.is-selected {
          background: rgba(99, 102, 241, 0.12);
        }
        .report-icon {
          width: 32px; height: 32px; flex-shrink: 0;
          border-radius: var(--radius-md);
          display: grid; place-items: center;
          background: var(--bg-sunken); color: var(--text-secondary);
        }
        .report-option.is-selected .report-icon {
          background: var(--brand-500); color: #fff;
        }
        .report-text { display: flex; flex-direction: column; min-width: 0; }
        .report-text strong { font-size: var(--text-base); font-weight: var(--weight-semibold); }
        .report-text span { font-size: var(--text-xs); color: var(--text-muted); line-height: 1.4; }
        @media (max-width: 1000px) {
          .report-layout { grid-template-columns: 1fr; }
          .report-picker { position: static; flex-direction: row; overflow-x: auto; }
          .report-option { min-width: 220px; }
        }
      `}</style>
    </>
  );
}

function renderCell(key: string, value: unknown): React.ReactNode {
  if (value == null || value === '') return <span className="muted">—</span>;

  if (typeof value === 'boolean') {
    // "At risk" reads as a warning, not as a neutral yes.
    return <Badge tone={value ? 'danger' : 'neutral'}>{value ? 'Yes' : 'No'}</Badge>;
  }

  if (key.toLowerCase().includes('percentage')) {
    const number = Number(value);
    const tone = number >= 90 ? 'success' : number >= 75 ? 'warning' : 'danger';
    return <Badge tone={tone}>{number}%</Badge>;
  }

  const text = String(value);

  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleString()}</span>;
  }
  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleDateString()}</span>;
  }

  return text;
}

function humanise(key: string) {
  const spaced = key.replace(/([A-Z])/g, ' $1').replace(/Utc$/i, '').trim();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

function shortName(name: string) {
  const parts = name.split(' ');
  return parts.length > 1 ? `${parts[0]} ${parts[parts.length - 1][0]}.` : name;
}

function isoDate(offsetDays: number) {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  return date.toISOString().slice(0, 10);
}
