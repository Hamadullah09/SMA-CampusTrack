import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import { Badge, Button, Card, ErrorState, LoadingState, useToast } from '@/components/ui';

interface Setting {
  id: number;
  key: string;
  category: string;
  displayName: string;
  description?: string;
  dataType: string;
  value?: string;
  defaultValue?: string;
  isEditable: boolean;
  isSecret: boolean;
}

/**
 * Runtime settings, grouped by what they affect.
 *
 * These are the values a school genuinely tunes after going live — the late threshold, the
 * RSSI floor, when the daily report goes out — so the screen shows each one's meaning and
 * its default rather than a bare key-value grid.
 */
export function SettingsPage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [edited, setEdited] = useState<Record<string, string>>({});
  const canManage = can(P.settingsManage);

  const settings = useQuery({
    queryKey: ['settings'],
    queryFn: async () => (await api.get<Setting[]>('/settings')).data,
  });

  const save = useMutation({
    mutationFn: () =>
      api.put('/settings', Object.entries(edited).map(([key, value]) => ({ key, value }))),
    onSuccess: () => {
      toast.success('Settings saved', 'Changes take effect immediately.');
      setEdited({});
      void queryClient.invalidateQueries({ queryKey: ['settings'] });
    },
    onError: (error) => toast.error('Could not save settings', describeError(error)),
  });

  const groups = useMemo(() => {
    const map = new Map<string, Setting[]>();
    for (const setting of settings.data ?? []) {
      const list = map.get(setting.category) ?? [];
      list.push(setting);
      map.set(setting.category, list);
    }
    return [...map.entries()];
  }, [settings.data]);

  const dirtyCount = Object.keys(edited).length;

  if (settings.isLoading) return <Card><LoadingState rows={10} /></Card>;

  if (settings.isError) {
    return (
      <Card>
        <ErrorState message={describeError(settings.error)} onRetry={() => void settings.refetch()} />
      </Card>
    );
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Settings</h1>
          <p className="page-subtitle">
            How the school runs — changed here, applied without a redeploy
          </p>
        </div>

        {canManage && dirtyCount > 0 && (
          <div className="row">
            <Button onClick={() => setEdited({})} disabled={save.isPending}>Discard</Button>
            <Button variant="primary" loading={save.isPending} onClick={() => save.mutate()}>
              Save {dirtyCount} change{dirtyCount === 1 ? '' : 's'}
            </Button>
          </div>
        )}
      </div>

      <div className="stack">
        {groups.map(([category, items]) => (
          <Card key={category} title={describeCategory(category)} subtitle={explainCategory(category)}>
            <div className="stack">
              {items.map((setting) => {
                const current = edited[setting.key] ?? setting.value ?? setting.defaultValue ?? '';
                const isDirty = setting.key in edited;
                const isDefault = current === setting.defaultValue;

                return (
                  <div key={setting.key} className="setting-row">
                    <div className="grow">
                      <div className="row" style={{ gap: 'var(--space-2)' }}>
                        <strong style={{ fontSize: 'var(--text-base)' }}>{setting.displayName}</strong>
                        {isDirty && <Badge tone="warning">Unsaved</Badge>}
                        {!isDefault && !isDirty && <Badge tone="info">Customised</Badge>}
                      </div>
                      {setting.description && (
                        <p className="muted" style={{ fontSize: 'var(--text-sm)', marginTop: 2 }}>
                          {setting.description}
                        </p>
                      )}
                      {/* Showing the default lets someone undo a change without guessing. */}
                      {setting.defaultValue && !isDefault && (
                        <p className="muted" style={{ fontSize: 'var(--text-xs)', marginTop: 2 }}>
                          Default: {setting.defaultValue}
                        </p>
                      )}
                    </div>

                    <div style={{ width: 200, flexShrink: 0 }}>
                      <SettingInput
                        setting={setting}
                        value={current}
                        disabled={!canManage || !setting.isEditable}
                        onChange={(value) => setEdited((c) => ({ ...c, [setting.key]: value }))}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </Card>
        ))}
      </div>

      <style>{`
        .setting-row {
          display: flex;
          align-items: flex-start;
          gap: var(--space-4);
          padding: var(--space-3) 0;
          border-bottom: 1px solid var(--border-subtle);
        }
        .setting-row:last-child { border-bottom: none; }
        @media (max-width: 640px) {
          .setting-row { flex-direction: column; align-items: stretch; }
          .setting-row > div:last-child { width: 100% !important; }
        }
      `}</style>
    </>
  );
}

function SettingInput({
  setting, value, disabled, onChange,
}: { setting: Setting; value: string; disabled: boolean; onChange: (value: string) => void }) {
  if (setting.dataType === 'Boolean') {
    return (
      <label className="checkbox-row">
        <input
          type="checkbox"
          checked={value === 'true' || value === '1'}
          disabled={disabled}
          onChange={(e) => onChange(String(e.target.checked))}
        />
        <span>{value === 'true' || value === '1' ? 'On' : 'Off'}</span>
      </label>
    );
  }

  if (setting.dataType === 'Time') {
    return (
      <input
        className="input" type="time" value={value.slice(0, 5)} disabled={disabled}
        onChange={(e) => onChange(e.target.value)} aria-label={setting.displayName}
      />
    );
  }

  return (
    <input
      className="input"
      type={setting.dataType === 'Integer' || setting.dataType === 'Decimal' ? 'number' : 'text'}
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      aria-label={setting.displayName}
    />
  );
}

function describeCategory(category: string) {
  switch (category) {
    case 'Rfid': return 'RFID readers';
    case 'Attendance': return 'Attendance rules';
    case 'Notifications': return 'Notifications';
    case 'Academic': return 'Academic';
    case 'Security': return 'Security';
    default: return category;
  }
}

function explainCategory(category: string) {
  switch (category) {
    case 'Rfid':
      return 'How reads are grouped into movements, and when a reader counts as offline';
    case 'Attendance':
      return 'What counts as late, early or absent';
    case 'Notifications':
      return 'What parents are told, and when';
    case 'Academic':
      return 'Codes and thresholds used across the school';
    case 'Security':
      return 'Sign-in limits and how long sessions last';
    default:
      return undefined;
  }
}
