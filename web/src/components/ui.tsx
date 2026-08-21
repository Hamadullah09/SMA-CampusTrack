import {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState,
  type ButtonHTMLAttributes, type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';

/* ------------------------------------------------------------------ icons ---- */

/**
 * A small hand-rolled icon set. An icon library would add ~300 KB to ship perhaps twenty
 * glyphs; these are stroke-consistent SVGs sized from the surrounding font.
 */
export function Icon({ name, size = 16 }: { name: IconName; size?: number }) {
  const path = ICON_PATHS[name];
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={{ flexShrink: 0 }}
    >
      {path}
    </svg>
  );
}

export type IconName = keyof typeof ICON_PATHS;

const ICON_PATHS = {
  dashboard: <><rect x="3" y="3" width="7" height="9" rx="1" /><rect x="14" y="3" width="7" height="5" rx="1" /><rect x="14" y="12" width="7" height="9" rx="1" /><rect x="3" y="16" width="7" height="5" rx="1" /></>,
  users: <><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M22 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" /></>,
  user: <><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></>,
  teacher: <><path d="M22 10v6M2 10l10-5 10 5-10 5z" /><path d="M6 12v5c3 3 9 3 12 0v-5" /></>,
  rfid: <><circle cx="12" cy="12" r="2" /><path d="M8.5 8.5a5 5 0 0 0 0 7" /><path d="M15.5 15.5a5 5 0 0 0 0-7" /><path d="M5.5 5.5a9 9 0 0 0 0 13" /><path d="M18.5 18.5a9 9 0 0 0 0-13" /></>,
  calendar: <><rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></>,
  check: <path d="M20 6 9 17l-5-5" />,
  clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
  book: <><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" /><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" /></>,
  award: <><circle cx="12" cy="8" r="6" /><path d="m8.2 13.9-1.4 7.1 5.2-3 5.2 3-1.4-7.1" /></>,
  chart: <><path d="M3 3v18h18" /><path d="m19 9-5 5-4-4-3 3" /></>,
  bell: <><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" /><path d="M10.3 21a2 2 0 0 0 3.4 0" /></>,
  megaphone: <><path d="m3 11 18-5v12L3 14v-3z" /><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6" /></>,
  settings: <><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" /></>,
  shield: <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />,
  search: <><circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" /></>,
  plus: <><path d="M12 5v14M5 12h14" /></>,
  edit: <><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" /><path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4z" /></>,
  trash: <><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" /></>,
  close: <path d="M18 6 6 18M6 6l12 12" />,
  chevronLeft: <path d="m15 18-6-6 6-6" />,
  chevronRight: <path d="m9 18 6-6-6-6" />,
  chevronDown: <path d="m6 9 6 6 6-6" />,
  download: <><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><path d="M7 10l5 5 5-5M12 15V3" /></>,
  refresh: <><path d="M3 12a9 9 0 0 1 15-6.7L21 8" /><path d="M21 3v5h-5" /><path d="M21 12a9 9 0 0 1-15 6.7L3 16" /><path d="M3 21v-5h5" /></>,
  logout: <><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><path d="m16 17 5-5-5-5M21 12H9" /></>,
  login: <><path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4" /><path d="m10 17 5-5-5-5M15 12H3" /></>,
  alert: <><path d="M12 9v4M12 17h.01" /><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /></>,
  info: <><circle cx="12" cy="12" r="9" /><path d="M12 16v-4M12 8h.01" /></>,
  inbox: <><path d="M22 12h-6l-2 3h-4l-2-3H2" /><path d="M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.7 4H7.3a2 2 0 0 0-1.8 1.1z" /></>,
  moon: <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />,
  sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M6.3 17.7l-1.4 1.4M19.1 4.9l-1.4 1.4" /></>,
  menu: <path d="M3 6h18M3 12h18M3 18h18" />,
  door: <><path d="M13 4h3a2 2 0 0 1 2 2v14M2 20h20" /><path d="M13 2v20H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" /><circle cx="10" cy="12" r=".6" fill="currentColor" /></>,
  building: <><rect x="4" y="2" width="16" height="20" rx="2" /><path d="M9 22v-4h6v4M9 6h.01M15 6h.01M9 10h.01M15 10h.01M9 14h.01M15 14h.01" /></>,
  file: <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><path d="M14 2v6h6" /></>,
  activity: <path d="M22 12h-4l-3 9L9 3l-3 9H2" />,
} as const;

/* ---------------------------------------------------------------- button ---- */

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  icon?: IconName;
  block?: boolean;
}

export function Button({
  variant = 'secondary', size = 'md', loading, icon, block,
  children, className = '', disabled, ...rest
}: ButtonProps) {
  const classes = [
    'btn',
    `btn-${variant}`,
    size !== 'md' ? `btn-${size}` : '',
    !children && icon ? 'btn-icon' : '',
    block ? 'btn-block' : '',
    className,
  ].filter(Boolean).join(' ');

  return (
    <button className={classes} disabled={disabled || loading} {...rest}>
      {loading ? <span className="btn-spinner" /> : icon ? <Icon name={icon} /> : null}
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------ card ---- */

export function Card({
  title, subtitle, actions, children, flush, className = '',
}: {
  title?: ReactNode; subtitle?: ReactNode; actions?: ReactNode;
  children: ReactNode; flush?: boolean; className?: string;
}) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <header className="card-header">
          <div>
            {title && <h2 className="card-title">{title}</h2>}
            {subtitle && <p className="card-subtitle">{subtitle}</p>}
          </div>
          {actions && <div className="row">{actions}</div>}
        </header>
      )}
      <div className={flush ? 'card-body-flush' : 'card-body'}>{children}</div>
    </section>
  );
}

/* ------------------------------------------------------------------ stat ---- */

export function Stat({
  label, value, meta, icon, accent = 'brand', loading,
}: {
  label: string; value: ReactNode; meta?: ReactNode; icon?: IconName;
  accent?: 'brand' | 'success' | 'warning' | 'danger' | 'info'; loading?: boolean;
}) {
  if (loading) return <div className="skeleton skeleton-stat" />;

  return (
    <article className={`stat stat-accent-${accent}`}>
      <span className="stat-label">{label}</span>
      <span className="stat-value">{value}</span>
      {meta && <span className="stat-meta">{meta}</span>}
      {icon && <span className="stat-icon"><Icon name={icon} size={18} /></span>}
    </article>
  );
}

/* ----------------------------------------------------------------- badge ---- */

export function Badge({
  children, tone = 'neutral', dot, live, title,
}: {
  children: ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand';
  dot?: boolean; live?: boolean;
  /** Hover text explaining what the badge means, for anything not self-evident. */
  title?: string;
}) {
  return (
    <span className={`badge badge-${tone} ${live ? 'badge-live' : ''}`} title={title}>
      {(dot || live) && <span className="badge-dot" />}
      {children}
    </span>
  );
}

/* ---------------------------------------------------------------- states ---- */

export function LoadingState({ rows = 5 }: { rows?: number }) {
  return (
    <div style={{ padding: 'var(--space-4)' }} aria-busy="true" aria-live="polite">
      <span className="sr-only">Loading</span>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="skeleton skeleton-row" />
      ))}
    </div>
  );
}

export function EmptyState({
  title, message, icon = 'inbox', action,
}: { title: string; message?: string; icon?: IconName; action?: ReactNode }) {
  return (
    <div className="state-panel">
      <span className="state-icon"><Icon name={icon} size={22} /></span>
      <h3 className="state-title">{title}</h3>
      {message && <p className="state-message">{message}</p>}
      {action}
    </div>
  );
}

/**
 * The error state a user actually sees. It says what failed in plain words and offers the
 * action that most often fixes it, rather than surfacing a status code.
 */
export function ErrorState({
  title = 'Something went wrong', message, onRetry,
}: { title?: string; message?: string; onRetry?: () => void }) {
  return (
    <div className="state-panel state-error">
      <span className="state-icon"><Icon name="alert" size={22} /></span>
      <h3 className="state-title">{title}</h3>
      <p className="state-message">{message ?? 'Please try again in a moment.'}</p>
      {onRetry && <Button icon="refresh" onClick={onRetry}>Try again</Button>}
    </div>
  );
}

/* ----------------------------------------------------------------- modal ---- */

export function Modal({
  open, onClose, title, children, footer, size = 'md',
}: {
  open: boolean; onClose: () => void; title: ReactNode;
  children: ReactNode; footer?: ReactNode; size?: 'sm' | 'md' | 'lg';
}) {
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose();
    }

    document.addEventListener('keydown', onKeyDown);
    // Stop the page behind the dialog scrolling under it on mobile.
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    // Move focus into the dialog so keyboard and screen-reader users land in the right place.
    dialogRef.current?.focus();

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = previousOverflow;
    };
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={dialogRef}
        className={`modal ${size !== 'md' ? `modal-${size}` : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={typeof title === 'string' ? title : undefined}
        tabIndex={-1}
      >
        <header className="modal-header">
          <h2 className="modal-title">{title}</h2>
          <Button variant="ghost" size="sm" icon="close" onClick={onClose} aria-label="Close" />
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-footer">{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}

/** A destructive action should always require a second, deliberate confirmation. */
export function ConfirmDialog({
  open, title, message, confirmLabel = 'Confirm', danger, onConfirm, onCancel, loading,
}: {
  open: boolean; title: string; message: string; confirmLabel?: string;
  danger?: boolean; onConfirm: () => void; onCancel: () => void; loading?: boolean;
}) {
  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="sm"
      footer={
        <>
          <Button onClick={onCancel} disabled={loading}>Cancel</Button>
          <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm} loading={loading}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <p style={{ color: 'var(--text-secondary)' }}>{message}</p>
    </Modal>
  );
}

/* ---------------------------------------------------------------- toasts ---- */

interface Toast {
  id: number;
  title: string;
  message?: string;
  tone: 'success' | 'error' | 'warning' | 'info';
}

const ToastContext = createContext<{
  show: (toast: Omit<Toast, 'id'>) => void;
  success: (title: string, message?: string) => void;
  error: (title: string, message?: string) => void;
} | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback((toast: Omit<Toast, 'id'>) => {
    const id = nextId.current++;
    setToasts((current) => [...current, { ...toast, id }]);
    // Errors linger: a user who looked away should still be able to read what failed.
    const lifetime = toast.tone === 'error' ? 7000 : 4000;
    setTimeout(() => dismiss(id), lifetime);
  }, [dismiss]);

  const value = useMemo(() => ({
    show,
    success: (title: string, message?: string) => show({ title, message, tone: 'success' }),
    error: (title: string, message?: string) => show({ title, message, tone: 'error' }),
  }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-region" role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast-${toast.tone}`}>
            <Icon name={toast.tone === 'success' ? 'check' : toast.tone === 'error' ? 'alert' : 'info'} />
            <div className="grow">
              <div className="toast-title">{toast.title}</div>
              {toast.message && <div className="toast-message">{toast.message}</div>}
            </div>
            <button
              className="btn btn-ghost btn-sm btn-icon"
              onClick={() => dismiss(toast.id)}
              aria-label="Dismiss"
            >
              <Icon name="close" size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used inside a ToastProvider');
  return context;
}

/* ------------------------------------------------------------ pagination ---- */

export function Pagination({
  page, pageSize, totalCount, totalPages, onPageChange,
}: {
  page: number; pageSize: number; totalCount: number; totalPages: number;
  onPageChange: (page: number) => void;
}) {
  if (totalCount === 0) return null;

  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);

  return (
    <div className="pagination">
      <span className="pagination-info">
        Showing {first}–{last} of {totalCount.toLocaleString()}
      </span>
      <div className="pagination-controls">
        <Button
          size="sm" icon="chevronLeft" aria-label="Previous page"
          disabled={page <= 1} onClick={() => onPageChange(page - 1)}
        />
        <span className="pagination-info tabular" style={{ padding: '0 var(--space-2)' }}>
          Page {page} of {Math.max(totalPages, 1)}
        </span>
        <Button
          size="sm" icon="chevronRight" aria-label="Next page"
          disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}
        />
      </div>
    </div>
  );
}

/* --------------------------------------------------------------- helpers ---- */

export function initialsOf(name?: string) {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
}

export function Avatar({ name, url, size = 'md' }: { name?: string; url?: string; size?: 'sm' | 'md' | 'lg' }) {
  const className = `avatar ${size !== 'md' ? `avatar-${size}` : ''}`;
  if (url) return <img className={className} src={url} alt="" />;
  return <span className={className} aria-hidden="true">{initialsOf(name)}</span>;
}

/** Debounces a rapidly changing value — used so a search box does not fire on every keystroke. */
export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}
