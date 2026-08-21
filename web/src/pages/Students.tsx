import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import {
  Avatar, Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, Icon,
  LoadingState, Modal, Pagination, useDebounced, useToast,
} from '@/components/ui';

interface StudentListItem {
  id: number;
  studentCode: string;
  fullName: string;
  email?: string;
  phoneNumber?: string;
  sectionName?: string;
  className?: string;
  rollNumber?: string;
  status: string;
  rfidCard?: string;
  hasActiveCard: boolean;
  presenceState: string;
  attendancePercentage?: number;
  guardianCount: number;
}

export function StudentsPage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [cardFilter, setCardFilter] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<StudentListItem | null>(null);

  // Debounced so typing a name does not fire a request per keystroke.
  const debouncedSearch = useDebounced(search);

  const query = useQuery({
    queryKey: ['students', page, debouncedSearch, status, cardFilter],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<StudentListItem>>('/students', {
        params: {
          page,
          pageSize: 25,
          search: debouncedSearch || undefined,
          status: status || undefined,
          hasRfidTag: cardFilter === '' ? undefined : cardFilter === 'yes',
        },
      });
      return data;
    },
  });

  const remove = useMutation({
    mutationFn: (id: number) => api.delete(`/students/${id}`),
    onSuccess: () => {
      toast.success('Student removed', 'Their attendance history has been preserved.');
      setPendingDelete(null);
      void queryClient.invalidateQueries({ queryKey: ['students'] });
    },
    onError: (error) => toast.error('Could not remove the student', describeError(error)),
  });

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Students</h1>
          <p className="page-subtitle">
            {query.data ? `${query.data.totalCount.toLocaleString()} enrolled` : 'Loading records'}
          </p>
        </div>
        <div className="row">
          <a
            className="btn btn-secondary"
            href="/api/v1/reports/attendance?format=xlsx"
            target="_blank"
            rel="noreferrer"
          >
            <Icon name="download" /> Export
          </a>
          {can(P.studentsCreate) && (
            <Button variant="primary" icon="plus" onClick={() => setCreateOpen(true)}>
              Add student
            </Button>
          )}
        </div>
      </div>

      <Card flush>
        <div className="toolbar">
          <div className="search-box">
            <Icon name="search" size={15} />
            <input
              className="input"
              placeholder="Search by name, code or email"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);   // a new search always starts at page one
              }}
              aria-label="Search students"
            />
          </div>

          <select
            className="select"
            style={{ width: 'auto' }}
            value={status}
            onChange={(e) => { setStatus(e.target.value); setPage(1); }}
            aria-label="Filter by status"
          >
            <option value="">All statuses</option>
            <option value="Active">Active</option>
            <option value="Pending">Pending</option>
            <option value="Suspended">Suspended</option>
            <option value="Inactive">Inactive</option>
          </select>

          <select
            className="select"
            style={{ width: 'auto' }}
            value={cardFilter}
            onChange={(e) => { setCardFilter(e.target.value); setPage(1); }}
            aria-label="Filter by card"
          >
            <option value="">Any card status</option>
            <option value="yes">Has a card</option>
            <option value="no">No card assigned</option>
          </select>

          <div className="grow" />

          <Button
            size="sm" icon="refresh" aria-label="Refresh"
            onClick={() => void query.refetch()} loading={query.isFetching}
          />
        </div>

        {query.isLoading ? (
          <LoadingState rows={8} />
        ) : query.isError ? (
          <ErrorState message={describeError(query.error)} onRetry={() => void query.refetch()} />
        ) : !query.data || query.data.items.length === 0 ? (
          <EmptyState
            title={debouncedSearch ? 'No students match that search' : 'No students yet'}
            message={
              debouncedSearch
                ? 'Try a different name or code, or clear the filters.'
                : 'Add your first student to begin tracking attendance and movement.'
            }
            icon="users"
            action={
              can(P.studentsCreate) && !debouncedSearch ? (
                <Button variant="primary" icon="plus" onClick={() => setCreateOpen(true)}>
                  Add student
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th>Student</th>
                    <th>Class</th>
                    <th>Card</th>
                    <th>Right now</th>
                    <th>Attendance</th>
                    <th>Status</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((student) => (
                    <tr key={student.id}>
                      <td data-label="Student">
                        <div className="row">
                          <Avatar name={student.fullName} size="sm" />
                          <div className="event-body">
                            <strong>{student.fullName}</strong>
                            <span className="mono">{student.studentCode}</span>
                          </div>
                        </div>
                      </td>
                      <td data-label="Class">
                        {student.sectionName ?? <span className="muted">Not enrolled</span>}
                        {student.rollNumber && <span className="muted"> · #{student.rollNumber}</span>}
                      </td>
                      <td data-label="Card">
                        {student.hasActiveCard ? (
                          <span className="mono">{student.rfidCard}</span>
                        ) : (
                          <Badge tone="warning">No card</Badge>
                        )}
                      </td>
                      <td data-label="Right now">
                        <PresenceBadge state={student.presenceState} />
                      </td>
                      <td data-label="Attendance">
                        {student.attendancePercentage == null ? (
                          <span className="muted">—</span>
                        ) : (
                          <AttendanceCell value={student.attendancePercentage} />
                        )}
                      </td>
                      <td data-label="Status">
                        <Badge tone={student.status === 'Active' ? 'success' : 'neutral'}>
                          {student.status}
                        </Badge>
                      </td>
                      <td data-label="Actions">
                        <div className="table-actions">
                          {can(P.studentsDelete) && (
                            <Button
                              size="sm" variant="ghost" icon="trash"
                              aria-label={`Remove ${student.fullName}`}
                              onClick={() => setPendingDelete(student)}
                            />
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <Pagination
              page={query.data.page}
              pageSize={query.data.pageSize}
              totalCount={query.data.totalCount}
              totalPages={query.data.totalPages}
              onPageChange={setPage}
            />
          </>
        )}
      </Card>

      <CreateStudentModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={() => {
          setCreateOpen(false);
          void queryClient.invalidateQueries({ queryKey: ['students'] });
        }}
      />

      <ConfirmDialog
        open={pendingDelete !== null}
        title="Remove this student?"
        message={
          `${pendingDelete?.fullName} will be removed from active lists and their card revoked. ` +
          'Their attendance and movement history is kept for the school record.'
        }
        confirmLabel="Remove student"
        danger
        loading={remove.isPending}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => pendingDelete && remove.mutate(pendingDelete.id)}
      />
    </>
  );
}

function PresenceBadge({ state }: { state: string }) {
  if (state === 'OnCampus') return <Badge tone="success" dot>On site</Badge>;
  if (state === 'InRoom') return <Badge tone="info" dot>In class</Badge>;
  return <Badge tone="neutral" dot>Off site</Badge>;
}

/** Colour reinforces the number here; the number itself always carries the meaning. */
function AttendanceCell({ value }: { value: number }) {
  const tone = value >= 90 ? 'success' : value >= 75 ? 'warning' : 'danger';

  return (
    <div style={{ minWidth: 96 }}>
      <div className="row" style={{ justifyContent: 'space-between', marginBottom: 4 }}>
        <span className="tabular" style={{ fontSize: 'var(--text-sm)' }}>{value}%</span>
      </div>
      <div className={`progress progress-${tone}`}>
        <div className="progress-bar" style={{ width: `${Math.min(value, 100)}%` }} />
      </div>
    </div>
  );
}

function CreateStudentModal({
  open, onClose, onCreated,
}: { open: boolean; onClose: () => void; onCreated: () => void }) {
  const toast = useToast();
  const [form, setForm] = useState({
    firstName: '', lastName: '', email: '', phoneNumber: '',
    sectionId: '', rfidEpc: '', rollNumber: '',
  });
  const [credentials, setCredentials] = useState<{ userName: string; password?: string } | null>(null);

  const { data: sections } = useQuery({
    queryKey: ['sections'],
    queryFn: async () => (await api.get<Array<{ id: number; displayName: string }>>('/academics/sections')).data,
    enabled: open,
  });

  const create = useMutation({
    mutationFn: async () => {
      const { data } = await api.post('/students', {
        firstName: form.firstName,
        lastName: form.lastName,
        email: form.email || undefined,
        phoneNumber: form.phoneNumber || undefined,
        sectionId: form.sectionId ? Number(form.sectionId) : undefined,
        rollNumber: form.rollNumber || undefined,
        rfidEpc: form.rfidEpc || undefined,
      });
      return data as { userName: string; temporaryPassword?: string; code: string };
    },
    onSuccess: (data) => {
      // The generated password is shown once and never again, so it gets its own step
      // rather than a toast the user might miss.
      setCredentials({ userName: data.userName, password: data.temporaryPassword });
      toast.success('Student added', `${form.firstName} ${form.lastName} · ${data.code}`);
    },
    onError: (error) => toast.error('Could not add the student', describeError(error)),
  });

  function close() {
    setForm({ firstName: '', lastName: '', email: '', phoneNumber: '', sectionId: '', rfidEpc: '', rollNumber: '' });
    setCredentials(null);
    onClose();
  }

  if (credentials) {
    return (
      <Modal
        open={open}
        onClose={() => { close(); onCreated(); }}
        title="Student added"
        footer={<Button variant="primary" onClick={() => { close(); onCreated(); }}>Done</Button>}
      >
        <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">Save these details now</div>
            <div className="alert-body">
              The temporary password is shown only once. The student must change it at first sign-in.
            </div>
          </div>
        </div>

        <div className="stack">
          <div className="field">
            <span className="label">Username</span>
            <code className="input mono" style={{ display: 'flex', alignItems: 'center' }}>
              {credentials.userName}
            </code>
          </div>
          {credentials.password && (
            <div className="field">
              <span className="label">Temporary password</span>
              <code className="input mono" style={{ display: 'flex', alignItems: 'center' }}>
                {credentials.password}
              </code>
            </div>
          )}
        </div>
      </Modal>
    );
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title="Add a student"
      size="lg"
      footer={
        <>
          <Button onClick={close}>Cancel</Button>
          <Button
            variant="primary"
            loading={create.isPending}
            disabled={!form.firstName.trim() || !form.lastName.trim()}
            onClick={() => create.mutate()}
          >
            Add student
          </Button>
        </>
      }
    >
      <div className="form-grid">
        <div className="field">
          <label className="label label-required" htmlFor="firstName">First name</label>
          <input
            id="firstName" className="input" value={form.firstName}
            onChange={(e) => setForm({ ...form, firstName: e.target.value })} autoFocus
          />
        </div>
        <div className="field">
          <label className="label label-required" htmlFor="lastName">Last name</label>
          <input
            id="lastName" className="input" value={form.lastName}
            onChange={(e) => setForm({ ...form, lastName: e.target.value })}
          />
        </div>
        <div className="field">
          <label className="label" htmlFor="email">Email</label>
          <input
            id="email" className="input" type="email" value={form.email}
            onChange={(e) => setForm({ ...form, email: e.target.value })}
          />
          <span className="field-hint">Optional. Used for password resets.</span>
        </div>
        <div className="field">
          <label className="label" htmlFor="phone">Phone</label>
          <input
            id="phone" className="input" value={form.phoneNumber}
            onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })}
          />
        </div>
        <div className="field">
          <label className="label" htmlFor="section">Class and section</label>
          <select
            id="section" className="select" value={form.sectionId}
            onChange={(e) => setForm({ ...form, sectionId: e.target.value })}
          >
            <option value="">Not enrolled yet</option>
            {sections?.map((section) => (
              <option key={section.id} value={section.id}>{section.displayName}</option>
            ))}
          </select>
        </div>
        <div className="field">
          <label className="label" htmlFor="roll">Roll number</label>
          <input
            id="roll" className="input" value={form.rollNumber}
            onChange={(e) => setForm({ ...form, rollNumber: e.target.value })}
          />
        </div>
        <div className="field" style={{ gridColumn: '1 / -1' }}>
          <label className="label" htmlFor="epc">RFID card (EPC)</label>
          <input
            id="epc" className="input mono" value={form.rfidEpc} placeholder="E28011606000020C3F1A2B3C"
            onChange={(e) => setForm({ ...form, rfidEpc: e.target.value.toUpperCase() })}
          />
          <span className="field-hint">
            Optional. Scan or type the card number now, or assign one later from the Cards screen.
          </span>
        </div>
      </div>
    </Modal>
  );
}
