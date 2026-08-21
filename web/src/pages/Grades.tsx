import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Stat,
} from '@/components/ui';

interface AcademicRow {
  studentName: string;
  studentCode: string;
  subjectName: string;
  assessmentCount: number;
  averagePercentage: number;
  bestPercentage: number;
  lowestPercentage: number;
}

/**
 * The gradebook overview.
 *
 * Marks live in one table on the server regardless of whether an assignment, quiz or exam
 * produced them, so this screen can show a subject average that actually means something
 * rather than three separate partial views.
 */
export function GradesPage() {
  const [sectionId, setSectionId] = useState('');
  const [subjectId, setSubjectId] = useState('');

  const sections = useQuery({
    queryKey: ['sections'],
    queryFn: async () =>
      (await api.get<Array<{ id: number; displayName: string }>>('/academics/sections')).data,
  });

  const subjects = useQuery({
    queryKey: ['subjects'],
    queryFn: async () =>
      (await api.get<Array<{ id: number; name: string }>>('/academics/subjects')).data,
  });

  const report = useQuery({
    queryKey: ['grades', sectionId, subjectId],
    queryFn: async () => {
      const { data } = await api.get<{ count: number; items: AcademicRow[] }>('/reports/academic', {
        params: { sectionId: sectionId || undefined, subjectId: subjectId || undefined },
      });
      return data;
    },
  });

  const rows = report.data?.items ?? [];

  const summary = useMemo(() => {
    if (rows.length === 0) return null;

    const average = rows.reduce((sum, r) => sum + r.averagePercentage, 0) / rows.length;
    const passing = rows.filter((r) => r.averagePercentage >= 40).length;
    const struggling = rows.filter((r) => r.averagePercentage < 40).length;
    const assessments = rows.reduce((sum, r) => sum + r.assessmentCount, 0);

    return {
      average: Math.round(average * 10) / 10,
      passing,
      struggling,
      assessments,
    };
  }, [rows]);

  const query = new URLSearchParams(
    Object.entries({ sectionId, subjectId }).filter(([, v]) => v) as [string, string][],
  ).toString();

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Grades</h1>
          <p className="page-subtitle">
            Assignments, quizzes and exams combined into one view per subject
          </p>
        </div>

        <a
          className="btn btn-secondary"
          href={`/api/v1/reports/academic?format=xlsx${query ? `&${query}` : ''}`}
          target="_blank"
          rel="noreferrer"
        >
          <Icon name="download" /> Export
        </a>
      </div>

      {summary && (
        <div className="stat-grid" style={{ marginBottom: 'var(--space-4)' }}>
          <Stat
            label="Average across the board"
            value={`${summary.average}%`}
            icon="chart"
            accent={summary.average >= 75 ? 'success' : summary.average >= 50 ? 'warning' : 'danger'}
          />
          <Stat label="Meeting the pass mark" value={summary.passing} icon="check" accent="success" />
          <Stat
            label="Below the pass mark"
            value={summary.struggling}
            icon="alert"
            accent={summary.struggling > 0 ? 'danger' : 'success'}
          />
          <Stat label="Marks recorded" value={summary.assessments.toLocaleString()} icon="file" />
        </div>
      )}

      <Card flush>
        <div className="toolbar">
          <select
            className="select" style={{ width: 'auto' }} value={sectionId}
            onChange={(e) => setSectionId(e.target.value)} aria-label="Filter by section"
          >
            <option value="">All sections</option>
            {sections.data?.map((s) => <option key={s.id} value={s.id}>{s.displayName}</option>)}
          </select>

          <select
            className="select" style={{ width: 'auto' }} value={subjectId}
            onChange={(e) => setSubjectId(e.target.value)} aria-label="Filter by subject"
          >
            <option value="">All subjects</option>
            {subjects.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>

          <div className="grow" />

          <Button size="sm" icon="refresh" aria-label="Refresh"
            onClick={() => void report.refetch()} loading={report.isFetching} />
        </div>

        {report.isLoading ? (
          <LoadingState rows={8} />
        ) : report.isError ? (
          <ErrorState message={describeError(report.error)} onRetry={() => void report.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No marks published yet"
            message="Grades appear here once teachers mark assignments, quizzes or exams."
            icon="award"
          />
        ) : (
          <div className="table-wrap">
            <table className="table table-responsive">
              <thead>
                <tr>
                  <th>Student</th>
                  <th>Subject</th>
                  <th style={{ textAlign: 'right' }} className="hide-below-sm">Marks</th>
                  <th>Average</th>
                  <th style={{ textAlign: 'right' }} className="hide-below-md">Best</th>
                  <th style={{ textAlign: 'right' }} className="hide-below-md">Lowest</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={`${row.studentCode}-${row.subjectName}-${index}`}>
                    <td data-label="Student">
                      <div className="event-body">
                        <strong>{row.studentName}</strong>
                        <span className="mono">{row.studentCode}</span>
                      </div>
                    </td>
                    <td data-label="Subject">{row.subjectName}</td>
                    <td data-label="Marks" style={{ textAlign: 'right' }} className="hide-below-sm">
                      <span className="tabular">{row.assessmentCount}</span>
                    </td>
                    <td data-label="Average">
                      <AverageBar value={row.averagePercentage} />
                    </td>
                    <td data-label="Best" style={{ textAlign: 'right' }} className="hide-below-md">
                      <span className="tabular">{row.bestPercentage}%</span>
                    </td>
                    <td data-label="Lowest" style={{ textAlign: 'right' }} className="hide-below-md">
                      <span className="tabular muted">{row.lowestPercentage}%</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}

function AverageBar({ value }: { value: number }) {
  const tone = value >= 75 ? 'success' : value >= 50 ? 'warning' : 'danger';

  return (
    <div style={{ minWidth: 110 }}>
      <div className="row" style={{ justifyContent: 'space-between', marginBottom: 3 }}>
        <span className="tabular" style={{ fontSize: 'var(--text-sm)', fontWeight: 600 }}>
          {value}%
        </span>
        {/* Below the pass mark is the fact a head of year is scanning for. */}
        {value < 40 && <Badge tone="danger">Failing</Badge>}
      </div>
      <div className={`progress progress-${tone}`}>
        <div className="progress-bar" style={{ width: `${Math.min(value, 100)}%` }} />
      </div>
    </div>
  );
}
