import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import {
  Avatar, Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState,
  Modal, Pagination, useDebounced, useToast,
} from '@/components/ui';

interface Guardian {
  id: number;
  guardianCode: string;
  fullName: string;
  email?: string;
  phoneNumber?: string;
  alternatePhone?: string;
  occupation?: string;
  status: string;
  childCount: number;
  children: string[];
  hasPendingLinks: boolean;
}

interface StudentOption {
  id: number;
  fullName: string;
  studentCode: string;
  sectionName?: string;
}

const RELATIONSHIPS = [
  { value: 'Mother', label: 'Mother' },
  { value: 'Father', label: 'Father' },
  { value: 'Parent', label: 'Parent' },
  { value: 'Guardian', label: 'Guardian' },
  { value: 'Grandparent', label: 'Grandparent' },
  { value: 'Sibling', label: 'Sibling' },
  { value: 'Other', label: 'Other' },
];

/**
 * Parents and guardians.
 *
 * The link between a parent and a child is the whole product for them: until it exists and
 * is approved they see nothing, and once it exists they see their child's movements. So this
 * screen is built around managing those links, not around the parent's contact details.
 */
export function GuardiansPage() {
  const { can } = useAuth();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState(false);
  const [linking, setLinking] = useState<Guardian | null>(null);

  const debouncedSearch = useDebounced(search);
  const canManage = can(P.guardiansManageLinks);

  const guardians = useQuery({
    queryKey: ['guardians', page, debouncedSearch],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<Guardian>>('/guardians', {
        params: { page, pageSize: 25, search: debouncedSearch || undefined },
      });
      return data;
    },
  });

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Parents and guardians</h1>
          <p className="page-subtitle">
            {guardians.data
              ? `${guardians.data.totalCount.toLocaleString()} registered`
              : 'Loading'}
            {' · each may follow several children'}
          </p>
        </div>

        <div className="row">
          <Button icon="refresh" aria-label="Refresh"
            onClick={() => void guardians.refetch()} loading={guardians.isFetching} />
          {can(P.guardiansCreate) && (
            <Button variant="primary" icon="plus" onClick={() => setCreating(true)}>
              Add parent
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
              placeholder="Search by parent name, phone or child"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              aria-label="Search parents"
            />
          </div>
        </div>

        {guardians.isLoading ? (
          <LoadingState rows={8} />
        ) : guardians.isError ? (
          <ErrorState message={describeError(guardians.error)} onRetry={() => void guardians.refetch()} />
        ) : !guardians.data || guardians.data.items.length === 0 ? (
          <EmptyState
            title={debouncedSearch ? 'No parents match that search' : 'No parents registered yet'}
            message={
              debouncedSearch
                ? 'Try the child’s name instead — parents can be found that way too.'
                : 'Add parents so they receive arrival notifications and can follow their child.'
            }
            icon="user"
            action={
              can(P.guardiansCreate) && !debouncedSearch ? (
                <Button variant="primary" icon="plus" onClick={() => setCreating(true)}>Add parent</Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table-responsive">
                <thead>
                  <tr>
                    <th>Parent</th>
                    <th>Children</th>
                    <th className="hide-below-md">Phone</th>
                    <th className="hide-below-lg">Email</th>
                    <th>Access</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {guardians.data.items.map((guardian) => (
                    <tr key={guardian.id}>
                      <td data-label="Parent">
                        <div className="row">
                          <Avatar name={guardian.fullName} size="sm" />
                          <div className="event-body">
                            <strong>{guardian.fullName}</strong>
                            <span className="mono">{guardian.guardianCode}</span>
                          </div>
                        </div>
                      </td>
                      <td data-label="Children">
                        {guardian.children.length === 0 ? (
                          <span className="muted">None linked</span>
                        ) : (
                          <div className="row wrap" style={{ gap: 4 }}>
                            {guardian.children.map((child) => (
                              <Badge key={child} tone="info">{child}</Badge>
                            ))}
                          </div>
                        )}
                      </td>
                      <td data-label="Phone" className="hide-below-md">
                        {guardian.phoneNumber ?? <span className="muted">—</span>}
                      </td>
                      <td data-label="Email" className="hide-below-lg">
                        {guardian.email ?? <span className="muted">—</span>}
                      </td>
                      <td data-label="Access">
                        {guardian.hasPendingLinks ? (
                          <Badge tone="warning" title="This parent cannot see the child until the link is approved">
                            <Icon name="alert" size={11} /> Approval needed
                          </Badge>
                        ) : guardian.childCount > 0 ? (
                          <Badge tone="success">Active</Badge>
                        ) : (
                          <Badge tone="neutral">No children</Badge>
                        )}
                      </td>
                      <td data-label="Actions">
                        <div className="table-actions">
                          {canManage && (
                            <Button
                              size="sm" variant="secondary" icon="users"
                              onClick={() => setLinking(guardian)}
                            >
                              Children
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <Pagination
              page={guardians.data.page} pageSize={guardians.data.pageSize}
              totalCount={guardians.data.totalCount} totalPages={guardians.data.totalPages}
              onPageChange={setPage}
            />
          </>
        )}
      </Card>

      {creating && (
        <CreateGuardianDialog
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            void queryClient.invalidateQueries({ queryKey: ['guardians'] });
          }}
        />
      )}

      {linking && (
        <ManageChildrenDialog
          guardian={linking}
          onClose={() => setLinking(null)}
          onChanged={() => void queryClient.invalidateQueries({ queryKey: ['guardians'] })}
        />
      )}
    </>
  );
}

function CreateGuardianDialog({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const toast = useToast();

  const [form, setForm] = useState({
    firstName: '', lastName: '', email: '', phoneNumber: '', occupation: '',
  });
  const [childId, setChildId] = useState('');
  const [relationship, setRelationship] = useState('Parent');
  const [credentials, setCredentials] = useState<{ userName: string; password?: string } | null>(null);

  const students = useQuery({
    queryKey: ['students-picker'],
    queryFn: async () =>
      (await api.get<PagedResult<StudentOption>>('/students', { params: { pageSize: 300 } })).data.items,
  });

  const create = useMutation({
    mutationFn: async () => {
      const { data } = await api.post('/guardians', {
        ...form,
        // Linking at creation is the common case, and a staff-created link is approved
        // immediately, so the parent can use the app straight away.
        children: childId
          ? [{ studentId: Number(childId), relationship, receivesNotifications: true, canViewAcademics: true }]
          : undefined,
      });
      return data as { userName: string; temporaryPassword?: string; code: string };
    },
    onSuccess: (data) => {
      setCredentials({ userName: data.userName, password: data.temporaryPassword });
      toast.success('Parent added', data.code);
    },
    onError: (error) => toast.error('Could not add the parent', describeError(error)),
  });

  if (credentials) {
    return (
      <Modal
        open onClose={onCreated} title="Parent added"
        footer={<Button variant="primary" onClick={onCreated}>Done</Button>}
      >
        <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">Give these details to the parent now</div>
            <div className="alert-body">
              The temporary password is shown only once. They must change it at first sign-in.
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
      open onClose={onClose} title="Add a parent" size="lg"
      footer={
        <>
          <Button onClick={onClose} disabled={create.isPending}>Cancel</Button>
          <Button
            variant="primary" loading={create.isPending}
            disabled={!form.firstName.trim() || !form.lastName.trim() || !form.phoneNumber.trim()}
            onClick={() => create.mutate()}
          >
            Add parent
          </Button>
        </>
      }
    >
      <div className="form-grid">
        <div className="field">
          <label className="label label-required" htmlFor="g-first">First name</label>
          <input id="g-first" className="input" value={form.firstName} autoFocus
            onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
        </div>
        <div className="field">
          <label className="label label-required" htmlFor="g-last">Last name</label>
          <input id="g-last" className="input" value={form.lastName}
            onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
        </div>
        <div className="field">
          <label className="label label-required" htmlFor="g-phone">Phone</label>
          <input id="g-phone" className="input" type="tel" value={form.phoneNumber}
            onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} />
          <span className="field-hint">How the school reaches them first.</span>
        </div>
        <div className="field">
          <label className="label" htmlFor="g-email">Email</label>
          <input id="g-email" className="input" type="email" value={form.email}
            onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </div>
        <div className="field">
          <label className="label" htmlFor="g-child">Link a child</label>
          <select id="g-child" className="select" value={childId}
            onChange={(e) => setChildId(e.target.value)}>
            <option value="">Link later</option>
            {students.data?.map((student) => (
              <option key={student.id} value={student.id}>
                {student.fullName} — {student.studentCode}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label className="label" htmlFor="g-rel">Relationship</label>
          <select id="g-rel" className="select" value={relationship}
            disabled={!childId} onChange={(e) => setRelationship(e.target.value)}>
            {RELATIONSHIPS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
          </select>
        </div>
      </div>
    </Modal>
  );
}

function ManageChildrenDialog({
  guardian, onClose, onChanged,
}: { guardian: Guardian; onClose: () => void; onChanged: () => void }) {
  const toast = useToast();
  const [childId, setChildId] = useState('');
  const [relationship, setRelationship] = useState('Parent');
  const [canViewAcademics, setCanViewAcademics] = useState(true);

  const students = useQuery({
    queryKey: ['students-picker'],
    queryFn: async () =>
      (await api.get<PagedResult<StudentOption>>('/students', { params: { pageSize: 300 } })).data.items,
  });

  const link = useMutation({
    mutationFn: () =>
      api.post(`/guardians/${guardian.id}/children`, {
        studentId: Number(childId),
        relationship,
        receivesNotifications: true,
        canViewAcademics,
        isAuthorisedForPickup: true,
      }),
    onSuccess: () => {
      toast.success('Child linked', 'The parent can now follow this child.');
      setChildId('');
      onChanged();
      onClose();
    },
    onError: (error) => toast.error('Could not link the child', describeError(error)),
  });

  return (
    <Modal
      open onClose={onClose} title={`Children — ${guardian.fullName}`}
      footer={
        <>
          <Button onClick={onClose}>Close</Button>
          <Button
            variant="primary" loading={link.isPending} disabled={!childId}
            onClick={() => link.mutate()}
          >
            Link child
          </Button>
        </>
      }
    >
      <div className="stack">
        <div>
          <p className="label">Currently linked</p>
          {guardian.children.length === 0 ? (
            <p className="muted">No children are linked to this parent yet.</p>
          ) : (
            <div className="row wrap" style={{ marginTop: 'var(--space-2)' }}>
              {guardian.children.map((child) => (
                <Badge key={child} tone="info">{child}</Badge>
              ))}
            </div>
          )}
        </div>

        <hr style={{ border: 0, borderTop: '1px solid var(--border-subtle)' }} />

        <div className="field">
          <label className="label label-required" htmlFor="link-child">Link another child</label>
          <select id="link-child" className="select" value={childId}
            onChange={(e) => setChildId(e.target.value)}>
            <option value="">Choose a student</option>
            {students.data?.map((student) => (
              <option key={student.id} value={student.id}>
                {student.fullName} — {student.studentCode}
                {student.sectionName ? ` (${student.sectionName})` : ''}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label className="label" htmlFor="link-rel">Relationship</label>
          <select id="link-rel" className="select" value={relationship}
            onChange={(e) => setRelationship(e.target.value)}>
            {RELATIONSHIPS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
          </select>
        </div>

        {/* A guardian authorised only for pickup should see arrivals but not grades, so
            academic access is a deliberate choice rather than automatic. */}
        <label className="checkbox-row">
          <input
            type="checkbox" checked={canViewAcademics}
            onChange={(e) => setCanViewAcademics(e.target.checked)}
          />
          <span>Can see grades, assignments and results</span>
        </label>

        <p className="field-hint">
          Links created here are approved immediately, because the school is the authority on
          who a child’s guardian is.
        </p>
      </div>
    </Modal>
  );
}
