import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { useAuth } from '@/lib/auth';
import {
  Avatar, Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState,
  Modal, Pagination, useDebounced, useToast,
} from '@/components/ui';

interface UserRow {
  id: number;
  userName: string;
  fullName: string;
  email?: string;
  phoneNumber?: string;
  isActive: boolean;
  mustChangePassword: boolean;
  lastLoginAtUtc?: string;
  lockoutEnd?: string;
  roles: string[];
  profileType: string;
}

interface RoleRow {
  id: number;
  name: string;
  description?: string;
  isSystemRole: boolean;
  userCount: number;
  permissions: string[];
}

/**
 * Accounts and roles.
 *
 * This is the screen an administrator reaches for when something has gone wrong — someone is
 * locked out, someone has left, someone needs different access — so the actions that matter
 * in those moments (reset, deactivate, change roles) are one click from the row.
 */
export function UsersPage() {
  const [tab, setTab] = useState<'users' | 'roles'>('users');

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Users and roles</h1>
          <p className="page-subtitle">Accounts, access and password resets</p>
        </div>
      </div>

      <div className="tabs" style={{ marginBottom: 'var(--space-4)' }}>
        <button className={`tab ${tab === 'users' ? 'tab-active' : ''}`} onClick={() => setTab('users')}>
          Accounts
        </button>
        <button className={`tab ${tab === 'roles' ? 'tab-active' : ''}`} onClick={() => setTab('roles')}>
          Roles and permissions
        </button>
      </div>

      {tab === 'users' ? <UsersTab /> : <RolesTab />}
    </>
  );
}

function UsersTab() {
  const { user: currentUser } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState('');
  const [resetting, setResetting] = useState<UserRow | null>(null);
  const [managingRoles, setManagingRoles] = useState<UserRow | null>(null);

  const debouncedSearch = useDebounced(search);

  const users = useQuery({
    queryKey: ['users', page, debouncedSearch, roleFilter],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<UserRow>>('/users', {
        params: { page, pageSize: 25, search: debouncedSearch || undefined, role: roleFilter || undefined },
      });
      return data;
    },
  });

  const setActive = useMutation({
    mutationFn: ({ id, active }: { id: number; active: boolean }) =>
      api.post(`/users/${id}/activate?active=${active}`),
    onSuccess: (_, variables) => {
      toast.success(variables.active ? 'Account reactivated' : 'Account deactivated');
      void queryClient.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (error) => toast.error('Could not change the account', describeError(error)),
  });

  return (
    <Card flush>
      <div className="toolbar">
        <div className="search-box">
          <Icon name="search" size={15} />
          <input
            className="input" placeholder="Search by name, username or email"
            value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            aria-label="Search accounts"
          />
        </div>

        <select
          className="select" style={{ width: 'auto' }} value={roleFilter}
          onChange={(e) => { setRoleFilter(e.target.value); setPage(1); }}
          aria-label="Filter by role"
        >
          <option value="">All roles</option>
          <option value="SuperAdmin">Super admin</option>
          <option value="Admin">Admin</option>
          <option value="Teacher">Teacher</option>
          <option value="Student">Student</option>
          <option value="Guardian">Parent</option>
          <option value="Staff">Staff</option>
        </select>
      </div>

      {users.isLoading ? (
        <LoadingState rows={8} />
      ) : users.isError ? (
        <ErrorState message={describeError(users.error)} onRetry={() => void users.refetch()} />
      ) : !users.data || users.data.items.length === 0 ? (
        <EmptyState title="No accounts match" message="Try a different search or role." icon="users" />
      ) : (
        <>
          <div className="table-wrap">
            <table className="table table-responsive">
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Roles</th>
                  <th className="hide-below-md">Type</th>
                  <th className="hide-below-lg">Last sign-in</th>
                  <th>Status</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {users.data.items.map((row) => {
                  const isSelf = row.id === currentUser?.id;
                  const lockedOut = row.lockoutEnd && new Date(row.lockoutEnd) > new Date();

                  return (
                    <tr key={row.id}>
                      <td data-label="Account">
                        <div className="row">
                          <Avatar name={row.fullName} size="sm" />
                          <div className="event-body">
                            <strong>{row.fullName}</strong>
                            <span className="mono">{row.userName}</span>
                          </div>
                        </div>
                      </td>
                      <td data-label="Roles">
                        <div className="row wrap" style={{ gap: 4 }}>
                          {row.roles.map((role) => (
                            <Badge key={role} tone={role.includes('Admin') ? 'brand' : 'neutral'}>
                              {role}
                            </Badge>
                          ))}
                        </div>
                      </td>
                      <td data-label="Type" className="hide-below-md">{row.profileType}</td>
                      <td data-label="Last sign-in" className="hide-below-lg">
                        {row.lastLoginAtUtc
                          ? <span className="tabular">{new Date(row.lastLoginAtUtc).toLocaleDateString()}</span>
                          : <span className="muted">Never</span>}
                      </td>
                      <td data-label="Status">
                        {/* Lockout is shown distinctly from deactivation: one clears itself,
                            the other needs an administrator. */}
                        {lockedOut ? (
                          <Badge tone="warning" title="Locked after too many failed sign-ins">
                            Locked out
                          </Badge>
                        ) : row.isActive ? (
                          <Badge tone="success">Active</Badge>
                        ) : (
                          <Badge tone="neutral">Deactivated</Badge>
                        )}
                        {row.mustChangePassword && (
                          <Badge tone="info" title="Must set a new password at next sign-in">
                            &nbsp;Reset pending
                          </Badge>
                        )}
                      </td>
                      <td data-label="Actions">
                        <div className="table-actions">
                          <Button
                            size="sm" variant="ghost" icon="shield"
                            aria-label={`Manage roles for ${row.fullName}`}
                            onClick={() => setManagingRoles(row)}
                          />
                          <Button
                            size="sm" variant="ghost" icon="refresh"
                            aria-label={`Reset password for ${row.fullName}`}
                            onClick={() => setResetting(row)}
                          />
                          {/* Deactivating yourself is the one action an administrator
                              cannot undo from inside the product. */}
                          {!isSelf && (
                            <Button
                              size="sm" variant="ghost"
                              icon={row.isActive ? 'close' : 'check'}
                              aria-label={row.isActive ? 'Deactivate account' : 'Reactivate account'}
                              onClick={() => setActive.mutate({ id: row.id, active: !row.isActive })}
                            />
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <Pagination
            page={users.data.page} pageSize={users.data.pageSize}
            totalCount={users.data.totalCount} totalPages={users.data.totalPages}
            onPageChange={setPage}
          />
        </>
      )}

      <ResetPasswordDialog
        user={resetting}
        onClose={() => setResetting(null)}
        onDone={() => {
          setResetting(null);
          void queryClient.invalidateQueries({ queryKey: ['users'] });
        }}
      />

      <ManageRolesDialog
        user={managingRoles}
        onClose={() => setManagingRoles(null)}
        onDone={() => {
          setManagingRoles(null);
          void queryClient.invalidateQueries({ queryKey: ['users'] });
        }}
      />
    </Card>
  );
}

function ResetPasswordDialog({
  user, onClose, onDone,
}: { user: UserRow | null; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [password, setPassword] = useState('');
  const [requireChange, setRequireChange] = useState(true);

  const reset = useMutation({
    mutationFn: () =>
      api.post(`/users/${user!.id}/reset-password`, {
        newPassword: password,
        requireChangeOnNextLogin: requireChange,
      }),
    onSuccess: () => {
      toast.success('Password reset', 'All of their sessions have been ended.');
      setPassword('');
      onDone();
    },
    onError: (error) => toast.error('Could not reset the password', describeError(error)),
  });

  if (!user) return null;

  return (
    <Modal
      open
      onClose={onClose}
      title={`Reset password — ${user.fullName}`}
      size="sm"
      footer={
        <>
          <Button onClick={onClose} disabled={reset.isPending}>Cancel</Button>
          <Button
            variant="primary" loading={reset.isPending} disabled={password.length < 8}
            onClick={() => reset.mutate()}
          >
            Reset password
          </Button>
        </>
      }
    >
      <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
        <Icon name="info" />
        <div>
          <div className="alert-title">This ends their active sessions</div>
          <div className="alert-body">
            Anyone signed in as this account on any device will be signed out.
          </div>
        </div>
      </div>

      <div className="stack">
        <div className="field">
          <label className="label label-required" htmlFor="new-password">New password</label>
          <input
            id="new-password" className="input" type="text" value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="At least 8 characters"
          />
          <span className="field-hint">
            Needs upper case, lower case and a digit. Read it to them, do not email it.
          </span>
        </div>

        <label className="checkbox-row">
          <input
            type="checkbox" checked={requireChange}
            onChange={(e) => setRequireChange(e.target.checked)}
          />
          <span>Require them to choose their own password at next sign-in</span>
        </label>
      </div>
    </Modal>
  );
}

function ManageRolesDialog({
  user, onClose, onDone,
}: { user: UserRow | null; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [selected, setSelected] = useState<string[]>([]);
  const [seeded, setSeeded] = useState(false);

  const roles = useQuery({
    queryKey: ['roles'],
    queryFn: async () => (await api.get<RoleRow[]>('/roles')).data,
    enabled: Boolean(user),
  });

  if (user && !seeded) {
    setSelected(user.roles);
    setSeeded(true);
  }

  const save = useMutation({
    mutationFn: () => api.post(`/users/${user!.id}/roles`, selected),
    onSuccess: () => {
      toast.success('Roles updated', 'They will need to sign in again.');
      setSeeded(false);
      onDone();
    },
    onError: (error) => toast.error('Could not update roles', describeError(error)),
  });

  if (!user) return null;

  return (
    <Modal
      open
      onClose={() => { setSeeded(false); onClose(); }}
      title={`Roles — ${user.fullName}`}
      footer={
        <>
          <Button onClick={() => { setSeeded(false); onClose(); }} disabled={save.isPending}>Cancel</Button>
          <Button variant="primary" loading={save.isPending} onClick={() => save.mutate()}>
            Save roles
          </Button>
        </>
      }
    >
      <p className="muted" style={{ marginBottom: 'var(--space-4)' }}>
        Roles decide what this person can do. Changing them signs them out so the new access
        takes effect immediately.
      </p>

      <div className="stack">
        {roles.data?.map((role) => (
          <label className="checkbox-row" key={role.id}>
            <input
              type="checkbox"
              checked={selected.includes(role.name)}
              onChange={(e) =>
                setSelected((current) =>
                  e.target.checked
                    ? [...current, role.name]
                    : current.filter((r) => r !== role.name))
              }
            />
            <div>
              <strong>{role.name}</strong>
              {role.description && (
                <div className="muted" style={{ fontSize: 'var(--text-sm)' }}>{role.description}</div>
              )}
            </div>
          </label>
        ))}
      </div>
    </Modal>
  );
}

function RolesTab() {
  const roles = useQuery({
    queryKey: ['roles'],
    queryFn: async () => (await api.get<RoleRow[]>('/roles')).data,
  });

  const catalogue = useQuery({
    queryKey: ['permission-catalogue'],
    queryFn: async () =>
      (await api.get<Array<{ group: string; permissions: Array<{ name: string; displayName: string }> }>>(
        '/roles/permissions')).data,
  });

  const [expanded, setExpanded] = useState<number | null>(null);

  if (roles.isLoading) return <Card><LoadingState rows={6} /></Card>;

  if (roles.isError) {
    return <Card><ErrorState message={describeError(roles.error)} onRetry={() => void roles.refetch()} /></Card>;
  }

  return (
    <div className="stack">
      {roles.data?.map((role) => (
        <Card
          key={role.id}
          title={role.name}
          subtitle={role.description}
          actions={
            <div className="row">
              <Badge tone="neutral">{role.userCount} user{role.userCount === 1 ? '' : 's'}</Badge>
              <Badge tone="brand">
                {role.name === 'SuperAdmin' ? 'All permissions' : `${role.permissions.length} permissions`}
              </Badge>
              <Button
                size="sm"
                onClick={() => setExpanded(expanded === role.id ? null : role.id)}
              >
                {expanded === role.id ? 'Hide' : 'Show'} permissions
              </Button>
            </div>
          }
        >
          {expanded === role.id && (
            <div className="stack">
              {/* SuperAdmin's grants are enforced in code so the school cannot lock itself
                  out; showing an editable list would imply otherwise. */}
              {role.name === 'SuperAdmin' ? (
                <div className="alert alert-info">
                  <Icon name="info" />
                  <div>
                    <div className="alert-title">This role always holds every permission</div>
                    <div className="alert-body">
                      It exists so a school can never lock itself out of its own system, and
                      cannot be restricted.
                    </div>
                  </div>
                </div>
              ) : (
                catalogue.data?.map((group) => {
                  const held = group.permissions.filter((p) => role.permissions.includes(p.name));
                  if (held.length === 0) return null;

                  return (
                    <div key={group.group}>
                      <p className="label">{group.group}</p>
                      <div className="row wrap" style={{ gap: 4, marginTop: 4 }}>
                        {held.map((permission) => (
                          <Badge key={permission.name} tone="success">
                            <Icon name="check" size={10} /> {permission.displayName.split(' (')[0]}
                          </Badge>
                        ))}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          )}
        </Card>
      ))}
    </div>
  );
}
