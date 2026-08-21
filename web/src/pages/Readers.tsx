import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Modal, useToast,
} from '@/components/ui';

interface Reader {
  id: number;
  deviceId: string;
  name: string;
  model: string;
  statusName: string;
  locationName: string;
  ipAddress?: string;
  firmwareVersion?: string;
  secondsSinceHeartbeat?: number;
  lastErrorMessage?: string;
  antennaCount: number;
  eventsToday: number;
}

export function ReadersPage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [issuedKey, setIssuedKey] = useState<{ deviceId: string; apiKey: string } | null>(null);

  const query = useQuery({
    queryKey: ['rfid', 'readers'],
    queryFn: async () => (await api.get<Reader[]>('/rfid/readers')).data,
    refetchInterval: 30_000,
  });

  const issueKey = useMutation({
    mutationFn: async (id: number) =>
      (await api.post<{ deviceId: string; apiKey: string }>(`/rfid/readers/${id}/api-key`)).data,
    onSuccess: (data) => {
      // Shown once and only once: the server keeps a hash, not the key.
      setIssuedKey(data);
      void queryClient.invalidateQueries({ queryKey: ['rfid', 'readers'] });
    },
    onError: (error) => toast.error('Could not issue a key', describeError(error)),
  });

  const readers = query.data ?? [];
  const offline = readers.filter((r) => r.statusName === 'Offline').length;

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">RFID readers</h1>
          <p className="page-subtitle">
            {readers.length} device(s) · {readers.length - offline} online
          </p>
        </div>
        <Button icon="refresh" onClick={() => void query.refetch()} loading={query.isFetching}>
          Refresh
        </Button>
      </div>

      <Card flush>
        {query.isLoading ? (
          <LoadingState rows={5} />
        ) : query.isError ? (
          <ErrorState message={describeError(query.error)} onRetry={() => void query.refetch()} />
        ) : readers.length === 0 ? (
          <EmptyState
            title="No readers registered"
            message="Register your D2184 readers here, then issue each one a device key so it can send reads."
            icon="rfid"
          />
        ) : (
          <div className="table-wrap">
            <table className="table table-responsive">
              <thead>
                <tr>
                  <th>Reader</th>
                  <th>Location</th>
                  <th>Status</th>
                  <th>Last contact</th>
                  <th>Today</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {readers.map((reader) => (
                  <tr key={reader.id}>
                    <td data-label="Reader">
                      <div className="event-body">
                        <strong>{reader.name}</strong>
                        <span className="mono">{reader.deviceId} · {reader.model}</span>
                      </div>
                    </td>
                    <td data-label="Location">{reader.locationName}</td>
                    <td data-label="Status">
                      <Badge
                        tone={reader.statusName === 'Online' ? 'success' : reader.statusName === 'Offline' ? 'danger' : 'warning'}
                        dot
                      >
                        {reader.statusName}
                      </Badge>
                    </td>
                    <td data-label="Last contact">
                      {reader.secondsSinceHeartbeat == null
                        ? <span className="muted">Never</span>
                        : <span className="tabular">{formatAge(reader.secondsSinceHeartbeat)} ago</span>}
                    </td>
                    <td data-label="Today" className="tabular">{reader.eventsToday.toLocaleString()}</td>
                    <td data-label="Actions">
                      <div className="table-actions">
                        {can(P.rfidManageReaders) && (
                          <Button
                            size="sm" variant="secondary"
                            loading={issueKey.isPending && issueKey.variables === reader.id}
                            onClick={() => issueKey.mutate(reader.id)}
                          >
                            Issue key
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={issuedKey !== null}
        onClose={() => setIssuedKey(null)}
        title="Device key issued"
        footer={<Button variant="primary" onClick={() => setIssuedKey(null)}>I have copied it</Button>}
      >
        <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">Copy this key now</div>
            <div className="alert-body">
              Only a hash is stored, so this key cannot be shown again. Paste it into the reader
              or gateway configuration before closing this dialog.
            </div>
          </div>
        </div>

        <div className="field">
          <span className="label">Device id</span>
          <code className="input mono" style={{ display: 'flex', alignItems: 'center' }}>
            {issuedKey?.deviceId}
          </code>
        </div>

        <div className="field" style={{ marginTop: 'var(--space-3)' }}>
          <span className="label">API key</span>
          <code
            className="input mono"
            style={{ display: 'flex', alignItems: 'center', height: 'auto', padding: 'var(--space-3)', wordBreak: 'break-all' }}
          >
            {issuedKey?.apiKey}
          </code>
        </div>

        <p className="field-hint" style={{ marginTop: 'var(--space-3)' }}>
          The reader must send this as the <code>X-Device-Key</code> header, along with its
          device id in <code>X-Device-Id</code>.
        </p>
      </Modal>
    </>
  );
}

function formatAge(seconds: number) {
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  return `${Math.floor(seconds / 86400)}d`;
}
