import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import { useRealtimeEvent } from '@/lib/realtime';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Modal, useToast,
} from '@/components/ui';

interface Notification {
  id: number;
  category: string;
  priority: string;
  title: string;
  body: string;
  isRead: boolean;
  createdAtUtc: string;
  studentName?: string;
}

/** The signed-in user's inbox, plus the tool for sending a message to the school. */
export function NotificationsPage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [unreadOnly, setUnreadOnly] = useState(false);
  const [composing, setComposing] = useState(false);

  const notifications = useQuery({
    queryKey: ['notifications', unreadOnly],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<Notification>>('/notifications', {
        params: { pageSize: 50, unreadOnly: unreadOnly || undefined },
      });
      return data;
    },
  });

  // New notifications arrive over the hub, so the inbox updates while it is open.
  useRealtimeEvent('notification', () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] });
  });

  const markAllRead = useMutation({
    mutationFn: () => api.post('/notifications/read-all'),
    onSuccess: () => {
      toast.success('All marked as read');
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    },
  });

  const markRead = useMutation({
    mutationFn: (id: number) => api.post(`/notifications/${id}/read`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  });

  const items = notifications.data?.items ?? [];
  const unread = items.filter((n) => !n.isRead).length;

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Notifications</h1>
          <p className="page-subtitle">
            {unread > 0 ? `${unread} unread` : 'Everything read'}
          </p>
        </div>

        <div className="row">
          {unread > 0 && (
            <Button onClick={() => markAllRead.mutate()} loading={markAllRead.isPending}>
              Mark all read
            </Button>
          )}
          {can(P.notificationsSend) && (
            <Button variant="primary" icon="megaphone" onClick={() => setComposing(true)}>
              Send a message
            </Button>
          )}
        </div>
      </div>

      <Card flush>
        <div className="toolbar">
          <label className="checkbox-row">
            <input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)} />
            <span>Unread only</span>
          </label>
          <div className="grow" />
          <Button size="sm" icon="refresh" aria-label="Refresh"
            onClick={() => void notifications.refetch()} loading={notifications.isFetching} />
        </div>

        {notifications.isLoading ? (
          <LoadingState rows={6} />
        ) : notifications.isError ? (
          <ErrorState message={describeError(notifications.error)}
            onRetry={() => void notifications.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState
            title={unreadOnly ? 'Nothing unread' : 'No notifications yet'}
            message="Arrivals, absences, results and school messages appear here."
            icon="bell"
          />
        ) : (
          <ul className="event-feed" style={{ maxHeight: 'none' }}>
            {items.map((notification) => {
              const { icon, tone } = visualFor(notification.category);

              return (
                <li
                  key={notification.id}
                  style={{
                    background: notification.isRead ? undefined : 'var(--bg-hover)',
                    cursor: notification.isRead ? undefined : 'pointer',
                  }}
                  onClick={() => !notification.isRead && markRead.mutate(notification.id)}
                >
                  <span className={`event-dot event-${tone}`}>
                    <Icon name={icon} size={13} />
                  </span>

                  <div className="event-body">
                    <strong style={{ fontWeight: notification.isRead ? 500 : 700 }}>
                      {notification.title}
                    </strong>
                    <span style={{ whiteSpace: 'normal' }}>{notification.body}</span>
                  </div>

                  <div className="row" style={{ flexShrink: 0 }}>
                    {notification.priority === 'Critical' && <Badge tone="danger">Urgent</Badge>}
                    <time className="event-time tabular">{relative(notification.createdAtUtc)}</time>
                    {!notification.isRead && (
                      <span
                        style={{
                          width: 8, height: 8, borderRadius: '50%',
                          background: 'var(--brand-500)', flexShrink: 0,
                        }}
                        aria-label="Unread"
                      />
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      {composing && (
        <ComposeDialog
          onClose={() => setComposing(false)}
          onSent={() => {
            setComposing(false);
            void queryClient.invalidateQueries({ queryKey: ['notifications'] });
          }}
        />
      )}
    </>
  );
}

function ComposeDialog({ onClose, onSent }: { onClose: () => void; onSent: () => void }) {
  const toast = useToast();

  const [form, setForm] = useState({
    title: '', body: '', audience: 'Everyone', priority: 'Normal', sectionId: '',
  });

  const sections = useQuery({
    queryKey: ['sections'],
    queryFn: async () =>
      (await api.get<Array<{ id: number; displayName: string }>>('/academics/sections')).data,
  });

  const send = useMutation({
    mutationFn: () =>
      api.post<{ sent: number }>('/notifications/send', {
        title: form.title,
        body: form.body,
        audience: form.audience,
        priority: form.priority,
        category: form.priority === 'Critical' ? 'Emergency' : 'Announcement',
        sectionId: form.sectionId ? Number(form.sectionId) : undefined,
      }),
    onSuccess: (response) => {
      toast.success('Message sent', `Delivered to ${response.data.sent} recipient(s).`);
      onSent();
    },
    onError: (error) => toast.error('Could not send', describeError(error)),
  });

  return (
    <Modal
      open onClose={onClose} title="Send a message" size="lg"
      footer={
        <>
          <Button onClick={onClose} disabled={send.isPending}>Cancel</Button>
          <Button
            variant="primary" loading={send.isPending}
            disabled={!form.title.trim() || !form.body.trim()}
            onClick={() => send.mutate()}
          >
            Send now
          </Button>
        </>
      }
    >
      {/* Urgent messages ignore quiet hours on purpose, so the sender should know before
          choosing it. */}
      {form.priority === 'Critical' && (
        <div className="alert alert-warning" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">Urgent messages bypass quiet hours</div>
            <div className="alert-body">
              This will reach every recipient immediately, including outside school hours.
            </div>
          </div>
        </div>
      )}

      <div className="stack">
        <div className="field">
          <label className="label label-required" htmlFor="msg-title">Title</label>
          <input id="msg-title" className="input" value={form.title} autoFocus
            onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </div>

        <div className="field">
          <label className="label label-required" htmlFor="msg-body">Message</label>
          <textarea id="msg-body" className="textarea" rows={5} value={form.body}
            onChange={(e) => setForm({ ...form, body: e.target.value })} />
          <span className="field-hint">Keep it short — this appears on a lock screen.</span>
        </div>

        <div className="form-grid">
          <div className="field">
            <label className="label" htmlFor="msg-audience">Send to</label>
            <select id="msg-audience" className="select" value={form.audience}
              disabled={Boolean(form.sectionId)}
              onChange={(e) => setForm({ ...form, audience: e.target.value })}>
              <option value="Everyone">Everyone</option>
              <option value="Students">Students</option>
              <option value="Guardians">Parents</option>
              <option value="Teachers">Teachers</option>
              <option value="Staff">Staff</option>
            </select>
          </div>

          <div className="field">
            <label className="label" htmlFor="msg-section">Or one section</label>
            <select id="msg-section" className="select" value={form.sectionId}
              onChange={(e) => setForm({ ...form, sectionId: e.target.value })}>
              <option value="">Not section-specific</option>
              {sections.data?.map((s) => <option key={s.id} value={s.id}>{s.displayName}</option>)}
            </select>
            <span className="field-hint">Reaches those students and their parents.</span>
          </div>

          <div className="field">
            <label className="label" htmlFor="msg-priority">Priority</label>
            <select id="msg-priority" className="select" value={form.priority}
              onChange={(e) => setForm({ ...form, priority: e.target.value })}>
              <option value="Normal">Normal</option>
              <option value="High">High</option>
              <option value="Critical">Urgent — bypasses quiet hours</option>
            </select>
          </div>
        </div>
      </div>
    </Modal>
  );
}

function visualFor(category: string): { icon: 'login' | 'logout' | 'alert' | 'award' | 'megaphone' | 'bell' | 'file'; tone: string } {
  switch (category) {
    case 'SchoolEntry': return { icon: 'login', tone: 'in' };
    case 'SchoolExit': return { icon: 'logout', tone: 'out' };
    case 'Absence':
    case 'LateArrival': return { icon: 'alert', tone: 'rejected' };
    case 'Grade':
    case 'Exam': return { icon: 'award', tone: 'in' };
    case 'Assignment':
    case 'Quiz': return { icon: 'file', tone: 'out' };
    case 'Emergency': return { icon: 'alert', tone: 'rejected' };
    case 'Announcement': return { icon: 'megaphone', tone: 'in' };
    default: return { icon: 'bell', tone: 'out' };
  }
}

function relative(iso: string) {
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.floor(diff / 60000);

  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes}m ago`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h ago`;
  if (minutes < 10080) return `${Math.floor(minutes / 1440)}d ago`;

  return new Date(iso).toLocaleDateString([], { day: 'numeric', month: 'short' });
}
