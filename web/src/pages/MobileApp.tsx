import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { api, API_BASE, describeError } from '@/api/client';
import { Button, Card, Icon, Badge, Modal, LoadingState, EmptyState, ErrorState, useToast, ConfirmDialog } from '@/components/ui';
import { PRODUCT_NAME } from '@/components/Brand';
import { P, useAuth } from '@/lib/auth';
import './mobile-app.css';

/**
 * The mobile app download page.
 *
 * Two audiences on one screen. Families need the .apk and a way to trust it; administrators
 * need to publish a new build and roll one back. The publishing half only renders for someone
 * holding the permission, so a parent sees a download page and nothing else.
 */

interface Release {
  id: number;
  version: string;
  buildNumber: number;
  fileName: string;
  sizeBytes: number;
  sha256: string;
  releaseNotes?: string | null;
  isCurrent: boolean;
  downloadCount: number;
  platform: string;
  publishedAtUtc: string;
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** The download URL is a plain link rather than an XHR: the browser handles a 200 MB
 *  binary far better than JavaScript holding it in memory, and it resumes if interrupted. */
const DOWNLOAD_URL = `${API_BASE}/app/download/Android`;

export function MobileAppPage() {
  const { can } = useAuth();
  const canManage = can(P.mobileAppManage);
  const queryClient = useQueryClient();

  const latest = useQuery({
    queryKey: ['app-latest'],
    queryFn: async () =>
      (await api.get<{ available: boolean; release?: Release }>('/app/latest')).data,
  });

  const releases = useQuery({
    queryKey: ['app-releases'],
    queryFn: async () => (await api.get<Release[]>('/app/releases')).data,
    enabled: canManage,
  });

  const refreshAll = () => {
    void queryClient.invalidateQueries({ queryKey: ['app-latest'] });
    void queryClient.invalidateQueries({ queryKey: ['app-releases'] });
  };

  if (latest.isLoading) return <LoadingState />;
  if (latest.isError) return <ErrorState message={describeError(latest.error)} onRetry={() => latest.refetch()} />;

  const release = latest.data?.available ? latest.data.release! : null;

  return (
    <div>
      <header className="page-header">
        <div>
          <h1 className="page-title">Mobile app</h1>
          <p className="page-subtitle">The {PRODUCT_NAME} app for parents and students</p>
        </div>
        {canManage && <PublishButton onDone={refreshAll} />}
      </header>

      <div className="app-layout">
        <Card className="app-download">
          <div className="app-icon"><Icon name="rfid" size={28} /></div>

          {release ? (
            <>
              <h2>Android app</h2>
              <p className="app-version">
                Version {release.version} <span className="muted">· build {release.buildNumber}</span>
              </p>

              <a className="btn btn-primary btn-lg app-cta" href={DOWNLOAD_URL} download>
                <Icon name="download" />
                Download APK
                <span className="app-size">{formatSize(release.sizeBytes)}</span>
              </a>

              {release.releaseNotes && (
                <div className="app-notes">
                  <h3>What&rsquo;s new</h3>
                  <p>{release.releaseNotes}</p>
                </div>
              )}

              {/* Sideloading skips every check the Play Store performs, so the checksum is
                  offered as the one way to confirm the file is the school's. */}
              <details className="app-verify">
                <summary>Verify this download</summary>
                <p>Compare the SHA-256 of the file you downloaded with the one below.</p>
                <code className="app-hash">{release.sha256}</code>
              </details>

              <p className="app-help">
                Android will warn you before installing an app from outside the Play Store.
                Allow installation from this site to continue.
              </p>
            </>
          ) : (
            <>
              <h2>Not published yet</h2>
              <p className="app-empty-body">
                {canManage
                  ? 'Publish a build and the download link will appear here for families.'
                  : 'The school has not published the app yet. Please check back later.'}
              </p>
            </>
          )}
        </Card>

        <Card className="app-guide">
          <h2>Installing on Android</h2>
          <ol className="app-steps">
            <li><strong>Download</strong> the APK using the button on this page.</li>
            <li><strong>Open</strong> the downloaded file from your notifications or Files app.</li>
            <li><strong>Allow</strong> installation from this source when Android asks.</li>
            <li><strong>Sign in</strong> with the username and password the school gave you.</li>
          </ol>
          <p className="app-help">
            Parents see their own children only. If something looks wrong, contact the school office.
          </p>
        </Card>
      </div>

      {canManage && (
        <ReleaseHistory
          releases={releases.data ?? []}
          loading={releases.isLoading}
          onChanged={refreshAll}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ publishing ---- */

function PublishButton({ onDone }: { onDone: () => void }) {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [version, setVersion] = useState('');
  const [buildNumber, setBuildNumber] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const toast = useToast();

  const publish = useMutation({
    mutationFn: async () => {
      const body = new FormData();
      body.append('file', file!);
      body.append('version', version.trim());
      body.append('buildNumber', buildNumber);
      if (notes.trim()) body.append('releaseNotes', notes.trim());

      // Multipart, not JSON: an apk is tens of megabytes and base64 would inflate it by a third.
      return api.post('/app/releases', body, { headers: { 'Content-Type': 'multipart/form-data' } });
    },
    onSuccess: () => {
      toast.success('Build published', 'Families downloading the app now get this version.');
      setOpen(false);
      setFile(null); setVersion(''); setBuildNumber(''); setNotes('');
      onDone();
    },
    onError: (caught) => setError(describeError(caught)),
  });

  function submit() {
    setError(null);
    if (!file) return setError('Choose the .apk file to publish.');
    if (!version.trim()) return setError('Enter the version, for example 1.4.0.');
    if (!buildNumber.trim() || Number.isNaN(Number(buildNumber))) return setError('Enter the build number.');
    publish.mutate();
  }

  return (
    <>
      <Button variant="primary" icon="plus" onClick={() => setOpen(true)}>Publish a build</Button>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Publish a build"
        footer={
          <>
            <Button onClick={() => setOpen(false)} disabled={publish.isPending}>Cancel</Button>
            <Button variant="primary" onClick={submit} loading={publish.isPending}>Publish</Button>
          </>
        }
      >
        <div className="form-grid">
          <div className="field">
            <label className="label" htmlFor="apk">APK file</label>
            <input
              id="apk"
              className="input"
              type="file"
              accept=".apk,application/vnd.android.package-archive"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            {file && <p className="field-hint">{file.name} · {formatSize(file.size)}</p>}
          </div>

          <div className="field">
            <label className="label" htmlFor="version">Version</label>
            <input id="version" className="input" placeholder="1.4.0"
              value={version} onChange={(e) => setVersion(e.target.value)} />
          </div>

          <div className="field">
            <label className="label" htmlFor="build">Build number</label>
            <input id="build" className="input" type="number" placeholder="14"
              value={buildNumber} onChange={(e) => setBuildNumber(e.target.value)} />
            <p className="field-hint">Must be higher than the last published build.</p>
          </div>

          <div className="field">
            <label className="label" htmlFor="notes">What&rsquo;s new</label>
            <textarea id="notes" className="textarea" rows={3} placeholder="Optional, shown to families"
              value={notes} onChange={(e) => setNotes(e.target.value)} />
          </div>
        </div>

        {error && <p className="field-error" role="alert">{error}</p>}
      </Modal>
    </>
  );
}

function ReleaseHistory({
  releases, loading, onChanged,
}: {
  releases: Release[]; loading: boolean; onChanged: () => void;
}) {
  const [removing, setRemoving] = useState<Release | null>(null);
  const toast = useToast();

  const promote = useMutation({
    mutationFn: (id: number) => api.post(`/app/releases/${id}/promote`),
    onSuccess: () => { toast.success('Build promoted', 'The download link now serves this build.'); onChanged(); },
    onError: (e) => toast.error('Could not promote that build', describeError(e)),
  });

  const remove = useMutation({
    mutationFn: (id: number) => api.delete(`/app/releases/${id}`),
    onSuccess: () => { toast.success('Build removed'); setRemoving(null); onChanged(); },
    onError: (e) => { toast.error('Could not remove that build', describeError(e)); setRemoving(null); },
  });

  if (loading) return <LoadingState rows={3} />;
  if (releases.length === 0) {
    return <EmptyState title="No builds yet" message="Published builds appear here with their download counts." />;
  }

  return (
    <Card className="app-history">
      <h2>Published builds</h2>
      <div className="table-wrap">
        <table className="table table-responsive">
          <thead>
            <tr>
              <th>Version</th><th>Build</th><th>Size</th>
              <th style={{ textAlign: 'right' }}>Downloads</th>
              <th>Published</th><th style={{ textAlign: 'right' }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {releases.map((r) => (
              <tr key={r.id}>
                <td data-label="Version">
                  <strong>{r.version}</strong>{' '}
                  {r.isCurrent && <Badge tone="success">Current</Badge>}
                </td>
                <td data-label="Build" className="mono">{r.buildNumber}</td>
                <td data-label="Size">{formatSize(r.sizeBytes)}</td>
                <td data-label="Downloads" style={{ textAlign: 'right' }}>{r.downloadCount}</td>
                <td data-label="Published">{new Date(r.publishedAtUtc).toLocaleDateString()}</td>
                <td data-label="Actions">
                  <div className="table-actions">
                    {!r.isCurrent && (
                      <>
                        <Button size="sm" variant="ghost" onClick={() => promote.mutate(r.id)}>
                          Promote
                        </Button>
                        <Button
                          size="sm" variant="ghost" icon="trash"
                          aria-label={`Remove build ${r.buildNumber}`}
                          onClick={() => setRemoving(r)}
                        />
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ConfirmDialog
        open={removing !== null}
        danger
        loading={remove.isPending}
        title="Remove this build?"
        message={`Build ${removing?.buildNumber} (${removing?.version}) and its file will be deleted permanently.`}
        confirmLabel="Remove build"
        onConfirm={() => removing && remove.mutate(removing.id)}
        onCancel={() => setRemoving(null)}
      />
    </Card>
  );
}
