import { Badge, Button } from '@/components/ui';
import { api } from '@/api/client';
import { P } from '@/lib/auth';
import type { ResourceConfig } from '@/components/resource/types';

/** Assessment, communication and administration resources. */

const AUDIENCES = [
  { value: 'Everyone', label: 'Everyone' },
  { value: 'Students', label: 'Students' },
  { value: 'Guardians', label: 'Parents' },
  { value: 'Teachers', label: 'Teachers' },
  { value: 'Staff', label: 'Staff' },
];

const PRIORITIES = [
  { value: 'Normal', label: 'Normal' },
  { value: 'High', label: 'High' },
  { value: 'Critical', label: 'Urgent' },
];

export const assignmentsResource: ResourceConfig = {
  title: 'Assignments',
  singular: 'assignment',
  endpoint: '/assignments',
  searchPlaceholder: 'Search assignments',
  permissions: {
    view: P.assignmentsView,
    create: P.assignmentsCreate,
    edit: 'assignments.edit',
    delete: 'assignments.delete',
  },
  filters: [
    {
      name: 'sectionId', label: 'All sections',
      optionsFrom: { endpoint: '/academics/sections', labelKey: 'displayName' },
    },
    {
      name: 'status', label: 'All statuses',
      options: [
        { value: 'Draft', label: 'Draft' },
        { value: 'Published', label: 'Published' },
        { value: 'Closed', label: 'Closed' },
      ],
    },
  ],
  columns: [
    {
      key: 'title',
      header: 'Assignment',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.title)}</strong>
          <span className="muted">{String(row.subjectName ?? '')} · {String(row.sectionName ?? '')}</span>
        </div>
      ),
    },
    {
      key: 'dueAtUtc',
      header: 'Due',
      render: (row) => {
        const due = new Date(String(row.dueAtUtc));
        const overdue = Boolean(row.isOverdue);

        return (
          <span className={overdue ? 'muted' : 'tabular'}>
            {due.toLocaleDateString([], { day: 'numeric', month: 'short' })}{' '}
            {due.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </span>
        );
      },
    },
    {
      key: 'submissionCount',
      header: 'Submitted',
      align: 'right',
      render: (row) => {
        const submitted = Number(row.submissionCount ?? 0);
        const expected = Number(row.expectedCount ?? 0);
        const graded = Number(row.gradedCount ?? 0);

        return (
          <div style={{ minWidth: 90 }}>
            <span className="tabular">{submitted} / {expected}</span>
            {graded > 0 && (
              <div className="muted" style={{ fontSize: 'var(--text-xs)' }}>{graded} marked</div>
            )}
          </div>
        );
      },
    },
    { key: 'teacherName', header: 'Set by', hideBelow: 'lg' },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Published' ? 'success' : row.status === 'Draft' ? 'warning' : 'neutral'}>
          {String(row.status)}
        </Badge>
      ),
    },
  ],
  fields: [
    { name: 'title', label: 'Title', type: 'text', required: true, fullWidth: true },
    {
      name: 'subjectId', label: 'Subject', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/subjects', labelKey: 'name' },
    },
    {
      name: 'sectionId', label: 'Section', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/sections', labelKey: 'displayName' },
    },
    {
      name: 'teacherId', label: 'Teacher', type: 'select',
      optionsFrom: { endpoint: '/teachers', labelKey: 'fullName', params: { pageSize: 200 } },
      hint: 'Leave blank to set it as yourself.',
    },
    { name: 'dueAtUtc', label: 'Due', type: 'datetime', required: true },
    { name: 'maxScore', label: 'Marks available', type: 'number', min: 1, defaultValue: 100 },
    { name: 'weight', label: 'Weight', type: 'number', min: 0, step: 0.1, defaultValue: 1 },
    {
      name: 'category', label: 'Counts as', type: 'select', defaultValue: 'Assignment',
      options: [
        { value: 'Assignment', label: 'Assignment' },
        { value: 'Project', label: 'Project' },
        { value: 'Practical', label: 'Practical' },
        { value: 'Participation', label: 'Participation' },
      ],
    },
    { name: 'allowLateSubmission', label: 'Accept late submissions', type: 'checkbox', defaultValue: true },
    { name: 'publishNow', label: 'Publish immediately and notify students', type: 'checkbox', createOnly: true },
    { name: 'instructions', label: 'Instructions', type: 'textarea', rows: 5, fullWidth: true },
  ],
  transformSubmit: (values) => ({
    ...values,
    subjectId: Number(values.subjectId),
    sectionId: Number(values.sectionId),
    teacherId: values.teacherId ? Number(values.teacherId) : undefined,
    maxScore: Number(values.maxScore || 100),
    weight: Number(values.weight || 1),
    // datetime-local gives a local string with no zone; the API expects UTC.
    dueAtUtc: values.dueAtUtc ? new Date(String(values.dueAtUtc)).toISOString() : undefined,
  }),
};

export const quizzesResource: ResourceConfig = {
  title: 'Quizzes',
  singular: 'quiz',
  endpoint: '/quizzes',
  permissions: { view: P.quizzesView, create: P.quizzesCreate, edit: P.quizzesCreate, delete: 'quizzes.delete' },
  filters: [
    {
      name: 'sectionId', label: 'All sections',
      optionsFrom: { endpoint: '/academics/sections', labelKey: 'displayName' },
    },
    {
      name: 'status', label: 'All statuses',
      options: [
        { value: 'Draft', label: 'Draft' },
        { value: 'Published', label: 'Published' },
        { value: 'Closed', label: 'Closed' },
      ],
    },
  ],
  columns: [
    {
      key: 'title',
      header: 'Quiz',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.title)}</strong>
          <span className="muted">{String(row.subjectName ?? '')} · {String(row.sectionName ?? '')}</span>
        </div>
      ),
    },
    {
      key: 'questionCount', header: 'Questions', align: 'right',
      render: (row) => <span className="tabular">{String(row.questionCount ?? 0)}</span>,
    },
    {
      key: 'attemptCount', header: 'Attempts', align: 'right',
      render: (row) => <span className="tabular">{String(row.attemptCount ?? 0)}</span>,
    },
    {
      key: 'awaitingGrading',
      header: 'To mark',
      align: 'right',
      render: (row) => {
        const waiting = Number(row.awaitingGrading ?? 0);
        return waiting > 0
          ? <Badge tone="warning">{waiting}</Badge>
          : <span className="muted">—</span>;
      },
    },
    { key: 'closesAtUtc', header: 'Closes', hideBelow: 'md' },
    {
      key: 'status', header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Published' ? 'success' : row.status === 'Draft' ? 'warning' : 'neutral'}>
          {String(row.status)}
        </Badge>
      ),
    },
  ],
  fields: [
    { name: 'title', label: 'Title', type: 'text', required: true, fullWidth: true },
    {
      name: 'subjectId', label: 'Subject', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/subjects', labelKey: 'name' },
    },
    {
      name: 'sectionId', label: 'Section', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/sections', labelKey: 'displayName' },
    },
    { name: 'opensAtUtc', label: 'Opens', type: 'datetime' },
    { name: 'closesAtUtc', label: 'Closes', type: 'datetime' },
    {
      name: 'durationMinutes', label: 'Time limit (minutes)', type: 'number', min: 1,
      hint: 'Leave blank for no time limit.',
    },
    { name: 'maxAttempts', label: 'Attempts allowed', type: 'number', min: 1, defaultValue: 1 },
    { name: 'passScore', label: 'Pass mark', type: 'number', min: 0, defaultValue: 0 },
    { name: 'shuffleQuestions', label: 'Shuffle questions', type: 'checkbox' },
    { name: 'showResultImmediately', label: 'Show the result straight away', type: 'checkbox', defaultValue: true },
    { name: 'instructions', label: 'Instructions', type: 'textarea', fullWidth: true },
  ],
  transformSubmit: (values) => ({
    ...values,
    subjectId: Number(values.subjectId),
    sectionId: Number(values.sectionId),
    maxAttempts: Number(values.maxAttempts || 1),
    passScore: Number(values.passScore || 0),
    durationMinutes: values.durationMinutes ? Number(values.durationMinutes) : undefined,
    opensAtUtc: values.opensAtUtc ? new Date(String(values.opensAtUtc)).toISOString() : undefined,
    closesAtUtc: values.closesAtUtc ? new Date(String(values.closesAtUtc)).toISOString() : undefined,
  }),
  emptyMessage: 'Create a quiz, then add its questions from the quiz detail screen.',
};

export const announcementsResource: ResourceConfig = {
  title: 'Announcements',
  singular: 'announcement',
  endpoint: '/announcements',
  permissions: {
    view: P.announcementsView,
    create: P.announcementsManage,
    edit: P.announcementsManage,
    delete: P.announcementsManage,
  },
  columns: [
    {
      key: 'title',
      header: 'Announcement',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.title)}</strong>
          <span className="muted">{stripHtml(String(row.body ?? '')).slice(0, 90)}</span>
        </div>
      ),
    },
    {
      key: 'audience', header: 'Audience',
      render: (row) => <Badge tone="brand">{humanise(String(row.audience))}</Badge>,
    },
    {
      key: 'priority', header: 'Priority',
      render: (row) => {
        const priority = String(row.priority);
        return (
          <Badge tone={priority === 'Critical' ? 'danger' : priority === 'High' ? 'warning' : 'neutral'}>
            {priority === 'Critical' ? 'Urgent' : priority}
          </Badge>
        );
      },
    },
    { key: 'publishAtUtc', header: 'Published', hideBelow: 'md' },
    {
      key: 'isPublished', header: 'Status',
      render: (row) => (
        <Badge tone={row.isPublished ? 'success' : 'warning'}>
          {row.isPublished ? 'Live' : 'Draft'}
        </Badge>
      ),
    },
  ],
  fields: [
    { name: 'title', label: 'Title', type: 'text', required: true, fullWidth: true },
    { name: 'body', label: 'Message', type: 'textarea', required: true, rows: 6, fullWidth: true },
    { name: 'audience', label: 'Send to', type: 'select', options: AUDIENCES, defaultValue: 'Everyone' },
    { name: 'priority', label: 'Priority', type: 'select', options: PRIORITIES, defaultValue: 'Normal' },
    { name: 'expiresAtUtc', label: 'Hide after', type: 'datetime' },
    {
      name: 'publishNow', label: 'Publish now', type: 'checkbox', defaultValue: true,
      hint: 'Leave unticked to save as a draft.', fullWidth: true,
    },
    {
      name: 'sendAsNotification', label: 'Also send as a notification', type: 'checkbox',
      defaultValue: true, fullWidth: true,
    },
  ],
  transformSubmit: (values) => ({
    ...values,
    expiresAtUtc: values.expiresAtUtc ? new Date(String(values.expiresAtUtc)).toISOString() : undefined,
  }),
};

export const eventsResource: ResourceConfig = {
  title: 'School events',
  singular: 'event',
  endpoint: '/events',
  paged: false,
  permissions: { view: P.eventsView, create: P.eventsManage, edit: P.eventsManage, delete: P.eventsManage },
  columns: [
    {
      key: 'title',
      header: 'Event',
      render: (row) => (
        <div className="row">
          <span
            style={{
              width: 4, height: 30, borderRadius: 2, flexShrink: 0,
              background: (row.colourHex as string) ?? 'var(--brand-500)',
            }}
          />
          <div className="event-body">
            <strong>{String(row.title)}</strong>
            {row.location ? <span className="muted">{String(row.location)}</span> : null}
          </div>
        </div>
      ),
    },
    {
      key: 'startAtUtc',
      header: 'When',
      render: (row) => {
        const start = new Date(String(row.startAtUtc));
        const end = new Date(String(row.endAtUtc));
        const allDay = Boolean(row.isAllDay);

        return (
          <span className="tabular">
            {start.toLocaleDateString([], { day: 'numeric', month: 'short' })}
            {!allDay && ` · ${start.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`}
            {end.toDateString() !== start.toDateString() &&
              ` – ${end.toLocaleDateString([], { day: 'numeric', month: 'short' })}`}
          </span>
        );
      },
    },
    {
      key: 'audience', header: 'For',
      render: (row) => <Badge tone="brand">{humanise(String(row.audience))}</Badge>,
    },
  ],
  fields: [
    { name: 'title', label: 'Event name', type: 'text', required: true, fullWidth: true },
    { name: 'startAtUtc', label: 'Starts', type: 'datetime', required: true },
    { name: 'endAtUtc', label: 'Ends', type: 'datetime', required: true },
    { name: 'location', label: 'Location', type: 'text' },
    { name: 'audience', label: 'For', type: 'select', options: AUDIENCES, defaultValue: 'Everyone' },
    { name: 'isAllDay', label: 'All-day event', type: 'checkbox' },
    { name: 'colourHex', label: 'Colour', type: 'colour', defaultValue: '#4f46e5' },
    { name: 'description', label: 'Description', type: 'textarea', fullWidth: true },
  ],
  transformSubmit: (values) => ({
    ...values,
    startAtUtc: values.startAtUtc ? new Date(String(values.startAtUtc)).toISOString() : undefined,
    endAtUtc: values.endAtUtc ? new Date(String(values.endAtUtc)).toISOString() : undefined,
  }),
};

export const examsResource: ResourceConfig = {
  title: 'Examinations',
  singular: 'exam',
  endpoint: '/exams',
  paged: false,
  permissions: { view: P.examsView, create: 'exams.manage', edit: 'exams.manage', delete: 'exams.manage' },
  columns: [
    { key: 'name', header: 'Exam', render: (row) => <strong>{String(row.name)}</strong> },
    { key: 'startDate', header: 'Starts' },
    { key: 'endDate', header: 'Ends' },
    {
      key: 'status', header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Completed' || row.status === 'ResultsPublished' ? 'success' : 'neutral'}>
          {humanise(String(row.status))}
        </Badge>
      ),
    },
    {
      key: 'resultsPublished', header: 'Results',
      render: (row) => (
        <Badge tone={row.resultsPublished ? 'success' : 'warning'}>
          {row.resultsPublished ? 'Published' : 'Withheld'}
        </Badge>
      ),
    },
  ],
  fields: [
    { name: 'name', label: 'Exam name', type: 'text', required: true, fullWidth: true },
    { name: 'startDate', label: 'Start date', type: 'date', required: true },
    { name: 'endDate', label: 'End date', type: 'date', required: true },
    { name: 'weight', label: 'Weight', type: 'number', min: 0, step: 0.1, defaultValue: 1 },
    { name: 'description', label: 'Description', type: 'textarea', fullWidth: true },
  ],
  transformSubmit: (values) => ({ ...values, weight: Number(values.weight || 1) }),
};

export const leaveResource: ResourceConfig = {
  title: 'Leave requests',
  singular: 'request',
  endpoint: '/leave',
  permissions: { view: P.leaveView, delete: 'leave.request' },
  filters: [
    {
      name: 'status', label: 'All statuses',
      options: [
        { value: 'Pending', label: 'Awaiting review' },
        { value: 'Approved', label: 'Approved' },
        { value: 'Rejected', label: 'Rejected' },
      ],
      defaultValue: 'Pending',
    },
  ],
  columns: [
    {
      key: 'studentName',
      header: 'For',
      render: (row) => (
        <strong>{String(row.studentName ?? row.teacherName ?? 'Unknown')}</strong>
      ),
    },
    {
      key: 'startDate',
      header: 'Dates',
      render: (row) => (
        <span className="tabular">
          {new Date(String(row.startDate)).toLocaleDateString()} –{' '}
          {new Date(String(row.endDate)).toLocaleDateString()}
          <span className="muted"> ({String(row.totalDays)} day{row.totalDays === 1 ? '' : 's'})</span>
        </span>
      ),
    },
    { key: 'reason', header: 'Reason', hideBelow: 'md' },
    {
      key: 'status', header: 'Status',
      render: (row) => {
        const status = String(row.status);
        return (
          <Badge tone={status === 'Approved' ? 'success' : status === 'Rejected' ? 'danger' : 'warning'}>
            {status === 'Pending' ? 'Awaiting review' : status}
          </Badge>
        );
      },
    },
  ],
  /**
   * Approving or rejecting is the whole point of this screen, so the decision lives on
   * the row itself rather than behind an edit dialog. Only pending requests offer the
   * buttons -- a decision already recorded is changed by the office, not re-clicked here.
   */
  rowActions: (row, { refresh }) => {
    if (row.status !== 'Pending') return null;

    const review = async (approved: boolean) => {
      await api.post(`/leave/${row.id}/review`, { approved });
      refresh();
    };

    return (
      <>
        <Button
          size="sm" variant="ghost" icon="check"
          aria-label="Approve request"
          onClick={() => void review(true)}
        />
        <Button
          size="sm" variant="ghost" icon="close"
          aria-label="Reject request"
          onClick={() => void review(false)}
        />
      </>
    );
  },

  emptyMessage: 'Leave requested by parents and staff appears here for review.',
};

export const auditResource: ResourceConfig = {
  title: 'Audit log',
  singular: 'entry',
  endpoint: '/audit',
  searchPlaceholder: 'Search by record, user or id',
  description: 'Every change, and who made it',
  permissions: { view: P.auditView },
  filters: [{ name: 'from', label: 'From', type: 'date' }],
  columns: [
    {
      key: 'occurredAtUtc',
      header: 'When',
      render: (row) => (
        <span className="tabular">{new Date(String(row.occurredAtUtc)).toLocaleString()}</span>
      ),
    },
    {
      key: 'userName',
      header: 'Who',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.userName ?? 'System')}</strong>
          {row.userRole ? <span className="muted">{String(row.userRole)}</span> : null}
        </div>
      ),
    },
    {
      key: 'action',
      header: 'Action',
      render: (row) => {
        const action = String(row.action);
        const tone = action === 'Delete' ? 'danger' : action === 'Create' ? 'success' : 'info';
        return <Badge tone={tone}>{action}</Badge>;
      },
    },
    {
      key: 'entityName',
      header: 'Record',
      render: (row) => (
        <div className="event-body">
          <strong>{humanise(String(row.entityName))}</strong>
          {row.entityId ? <span className="mono">#{String(row.entityId)}</span> : null}
        </div>
      ),
    },
    { key: 'affectedColumns', header: 'Fields changed', hideBelow: 'lg' },
    { key: 'ipAddress', header: 'From', hideBelow: 'lg' },
  ],
  // The before/after values are the point of an audit log, so they get a detail panel
  // rather than being truncated into a column.
  expandedRow: (row) => {
    const before = safeParse(row.oldValuesJson as string | undefined);
    const after = safeParse(row.newValuesJson as string | undefined);

    if (!before && !after) return <p className="muted">No field-level detail recorded.</p>;

    const keys = Array.from(new Set([...Object.keys(before ?? {}), ...Object.keys(after ?? {})]));

    return (
      <div className="table-wrap">
        <table className="table" style={{ fontSize: 'var(--text-sm)' }}>
          <thead>
            <tr><th>Field</th><th>Before</th><th>After</th></tr>
          </thead>
          <tbody>
            {keys.map((key) => (
              <tr key={key}>
                <td><strong>{humanise(key)}</strong></td>
                <td className="muted">{format(before?.[key])}</td>
                <td>{format(after?.[key])}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  },
  exportPath: '/api/v1/audit?format=csv',
  emptyMessage: 'Changes made in the system are recorded here.',
};

function safeParse(value?: string): Record<string, unknown> | null {
  if (!value) return null;
  try {
    return JSON.parse(value) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function format(value: unknown) {
  if (value == null || value === '') return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value).slice(0, 80);
}

function stripHtml(value: string) {
  return value.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim();
}

function humanise(value: string) {
  return value.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase()).trim();
}
