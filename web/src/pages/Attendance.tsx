import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Modal, Pagination, useToast,
} from '@/components/ui';

interface DailyRecord {
  id: number;
  studentId: number;
  studentName: string;
  studentCode: string;
  sectionName?: string;
  date: string;
  status: string;
  firstEntryAtUtc?: string;
  lastExitAtUtc?: string;
  lateMinutes: number;
  earlyLeaveMinutes: number;
  source: string;
  isManuallyAdjusted: boolean;
  remarks?: string;
}

const STATUSES = ['Present', 'Absent', 'Late', 'EarlyLeave', 'Excused', 'Leave', 'Partial'];

/**
 * The attendance register.
 *
 * Most rows here were produced by the readers rather than by a person, so the screen is built
 * around reviewing and correcting rather than data entry: the source of every record is shown,
 * and overriding an RFID-derived one requires a stated reason.
 */
export function AttendancePage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();

  const [page, setPage] = useState(1);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [status, setStatus] = useState('');
  const [correcting, setCorrecting] = useState<DailyRecord | null>(null);

  const slotId = params.get('slot');

  const query = useQuery({
    queryKey: ['attendance', page, date, status],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<DailyRecord>>('/attendance/daily', {
        params: { page, pageSize: 25, fromDate: date, toDate: date, status: status || undefined },
      });
      return data;
    },
  });

  const rows = query.data?.items ?? [];
  const counts = {
    present: rows.filter((r) => r.status === 'Present').length,
    late: rows.filter((r) => r.status === 'Late').length,
    absent: rows.filter((r) => r.status === 'Absent').length,
  };

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Attendance</h1>
          <p className="page-subtitle">
            {slotId
              ? 'Taking the register for a lesson'
              : 'Records derived from gate and classroom movement'}
          </p>
        </div>
        <a
          className="btn btn-secondary"
          href={`/api/v1/reports/attendance?format=xlsx&from=${date}&to=${date}`}
          target="_blank"
          rel="noreferrer"
        >
          <Icon name="download" /> Export
        </a>
      </div>

      {rows.length > 0 && (
        <div className="stat-grid" style={{ marginBottom: 'var(--space-4)' }}>
          <Badge tone="success">{counts.present} present</Badge>
          <Badge tone="warning">{counts.late} late</Badge>
          <Badge tone="danger">{counts.absent} absent</Badge>
        </div>
      )}

      <Card flush>
        <div className="toolbar">
          <div className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 'var(--space-2)' }}>
            <label className="label" htmlFor="date" style={{ margin: 0 }}>Date</label>
            <input
              id="date" className="input" type="date" style={{ width: 'auto' }}
              value={date} onChange={(e) => { setDate(e.target.value); setPage(1); }}
            />
          </div>

          <select
            className="select" style={{ width: 'auto' }} value={status}
            onChange={(e) => { setStatus(e.target.value); setPage(1); }}
            aria-label="Filter by status"
          >
            <option value="">All statuses</option>
            {STATUSES.map((s) => <option key={s} value={s}>{humanStatus(s)}</option>)}
          </select>

          <div className="grow" />

          <Button size="sm" icon="refresh" aria-label="Refresh"
            onClick={() => void query.refetch()} loading={query.isFetching} />
        </div>

        {query.isLoading ? (
          <LoadingState rows={8} />
        ) : query.isError ? (
          <ErrorState message={describeError(query.error)} onRetry={() => void query.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No attendance recorded for this day"
            message="Records appear automatically as students arrive, or once absences are finalised."
            icon="check"
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th>Student</th>
                    <th>Class</th>
                    <th>Status</th>
                    <th>Arrived</th>
                    <th>Left</th>
                    <th>Source</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((record) => (
                    <tr key={record.id}>
                      <td data-label="Student">
                        <div className="event-body">
                          <strong>{record.studentName}</strong>
                          <span className="mono">{record.studentCode}</span>
                        </div>
                      </td>
                      <td data-label="Class">{record.sectionName ?? <span className="muted">—</span>}</td>
                      <td data-label="Status">
                        <Badge tone={toneOf(record.status)}>{humanStatus(record.status)}</Badge>
                        {record.lateMinutes > 0 && (
                          <span className="muted" style={{ marginLeft: 6, fontSize: 'var(--text-sm)' }}>
                            +{record.lateMinutes}m
                          </span>
                        )}
                      </td>
                      <td data-label="Arrived" className="tabular">{timeOrDash(record.firstEntryAtUtc)}</td>
                      <td data-label="Left" className="tabular">{timeOrDash(record.lastExitAtUtc)}</td>
                      <td data-label="Source">
                        {/* Whether a person or a reader produced this record is the first
                            thing anyone reviewing attendance needs to know. */}
                        {record.isManuallyAdjusted ? (
                          <Badge tone="info" title={record.remarks}>Corrected</Badge>
                        ) : record.source === 'Rfid' ? (
                          <Badge tone="neutral"><Icon name="rfid" size={11} /> RFID</Badge>
                        ) : (
                          <Badge tone="neutral">{record.source}</Badge>
                        )}
                      </td>
                      <td data-label="Actions">
                        <div className="table-actions">
                          {can(P.attendanceCorrect) && (
                            <Button size="sm" variant="ghost" icon="edit"
                              aria-label={`Correct attendance for ${record.studentName}`}
                              onClick={() => setCorrecting(record)} />
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {query.data && (
              <Pagination
                page={query.data.page} pageSize={query.data.pageSize}
                totalCount={query.data.totalCount} totalPages={query.data.totalPages}
                onPageChange={setPage}
              />
            )}
          </>
        )}
      </Card>

      <CorrectionModal
        record={correcting}
        onClose={() => setCorrecting(null)}
        onSaved={() => {
          setCorrecting(null);
          toast.success('Attendance updated', 'The change has been recorded in the audit trail.');
          void queryClient.invalidateQueries({ queryKey: ['attendance'] });
        }}
      />
    </>
  );
}

function CorrectionModal({
  record, onClose, onSaved,
}: { record: DailyRecord | null; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [status, setStatus] = useState('Present');
  const [reason, setReason] = useState('');

  const save = useMutation({
    mutationFn: () =>
      api.post('/attendance/mark', {
        studentId: record!.studentId,
        date: record!.date,
        status,
        reason,
        remarks: reason,
      }),
    onSuccess: onSaved,
    onError: (error) => toast.error('Could not update attendance', describeError(error)),
  });

  if (!record) return null;

  // The API refuses an override of an RFID record without a reason; the UI mirrors that
  // rather than letting the user discover it through a failed request.
  const reasonRequired = record.source === 'Rfid';
  const canSave = !reasonRequired || reason.trim().length > 0;

  return (
    <Modal
      open
      onClose={onClose}
      title={`Correct attendance — ${record.studentName}`}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button variant="primary" loading={save.isPending} disabled={!canSave}
            onClick={() => save.mutate()}>
            Save correction
          </Button>
        </>
      }
    >
      {reasonRequired && (
        <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="info" />
          <div>
            <div className="alert-title">This record came from the RFID readers</div>
            <div className="alert-body">
              Please explain why it is being changed. The correction is kept in the audit trail
              with your name and the original value.
            </div>
          </div>
        </div>
      )}

      <div className="stack">
        <div className="field">
          <span className="label">Currently recorded</span>
          <div className="row">
            <Badge tone={toneOf(record.status)}>{humanStatus(record.status)}</Badge>
            {record.firstEntryAtUtc && (
              <span className="muted">arrived {timeOrDash(record.firstEntryAtUtc)}</span>
            )}
          </div>
        </div>

        <div className="field">
          <label className="label label-required" htmlFor="new-status">Change to</label>
          <select id="new-status" className="select" value={status}
            onChange={(e) => setStatus(e.target.value)}>
            {STATUSES.map((s) => <option key={s} value={s}>{humanStatus(s)}</option>)}
          </select>
        </div>

        <div className="field">
          <label className={`label ${reasonRequired ? 'label-required' : ''}`} htmlFor="reason">
            Reason
          </label>
          <textarea
            id="reason" className="textarea" value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="e.g. Card was left at home; student signed in at reception."
          />
          {reasonRequired && !reason.trim() && (
            <span className="field-error"><Icon name="alert" size={12} /> A reason is required.</span>
          )}
        </div>
      </div>
    </Modal>
  );
}

function toneOf(status: string) {
  switch (status) {
    case 'Present': return 'success' as const;
    case 'Late':
    case 'Partial':
    case 'EarlyLeave': return 'warning' as const;
    case 'Absent':
    case 'Unexcused': return 'danger' as const;
    case 'Leave':
    case 'Excused': return 'info' as const;
    default: return 'neutral' as const;
  }
}

function humanStatus(status: string) {
  return status.replace(/([A-Z])/g, ' $1').trim();
}

function timeOrDash(iso?: string) {
  return iso
    ? new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : '—';
}
