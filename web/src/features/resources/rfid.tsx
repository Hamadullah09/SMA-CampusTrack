import { Badge, Icon } from '@/components/ui';
import { P } from '@/lib/auth';
import type { ResourceConfig } from '@/components/resource/types';
import { AssignCardButton, RevokeCardButton } from './CardActions';

/** RFID configuration: locations, cards and the movement log. */

const LOCATION_TYPES = [
  { value: 'MainGate', label: 'Main gate' },
  { value: 'ExitGate', label: 'Exit gate' },
  { value: 'Classroom', label: 'Classroom' },
  { value: 'Laboratory', label: 'Laboratory' },
  { value: 'Library', label: 'Library' },
  { value: 'ComputerLab', label: 'Computer lab' },
  { value: 'StaffRoom', label: 'Staff room' },
  { value: 'Cafeteria', label: 'Cafeteria' },
  { value: 'Auditorium', label: 'Auditorium' },
  { value: 'Playground', label: 'Playground' },
  { value: 'Hostel', label: 'Hostel' },
  { value: 'Transport', label: 'Transport' },
  { value: 'Other', label: 'Other' },
];

export const locationsResource: ResourceConfig = {
  title: 'RFID locations',
  singular: 'location',
  endpoint: '/rfid/locations',
  paged: false,
  description: 'Monitored places, and what movement there means',
  permissions: {
    view: P.rfidLocations,
    create: P.rfidManageLocations,
    delete: P.rfidManageLocations,
    edit: P.rfidManageLocations,
  },
  updateEndpoint: (row) => `/rfid/locations/${row.id}`,
  columns: [
    {
      key: 'name',
      header: 'Location',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.name)}</strong>
          <span className="mono">{String(row.code)}</span>
        </div>
      ),
    },
    {
      key: 'locationType',
      header: 'Type',
      render: (row) => <Badge tone="neutral">{humanise(String(row.locationType))}</Badge>,
    },
    {
      key: 'isCampusBoundary',
      header: 'Meaning',
      render: (row) =>
        // The single most important property of a location: whether crossing it means
        // arriving at or leaving school.
        row.isCampusBoundary ? (
          <Badge tone="brand" title="Movement here changes on-site presence and notifies parents">
            Campus boundary
          </Badge>
        ) : (
          <Badge tone="neutral">Internal</Badge>
        ),
    },
    { key: 'classroomName', header: 'Room', hideBelow: 'md' },
    {
      key: 'readerCount',
      header: 'Readers',
      align: 'right',
      render: (row) => {
        const total = Number(row.readerCount ?? 0);
        const online = Number(row.onlineReaders ?? 0);
        if (total === 0) return <Badge tone="warning">None</Badge>;

        return (
          <Badge tone={online === total ? 'success' : online === 0 ? 'danger' : 'warning'}>
            {online}/{total} online
          </Badge>
        );
      },
    },
    {
      key: 'notifyGuardians',
      header: 'Notifies',
      hideBelow: 'lg',
      render: (row) => (row.notifyGuardians ? <Badge tone="info">Parents</Badge> : <span className="muted">—</span>),
    },
  ],
  fields: [
    { name: 'name', label: 'Location name', type: 'text', required: true, placeholder: 'Main Gate' },
    {
      name: 'code', label: 'Code', type: 'text', required: true, placeholder: 'MAIN-GATE',
      readOnlyOnEdit: true,
    },
    { name: 'locationType', label: 'Type', type: 'select', required: true, options: LOCATION_TYPES },
    {
      name: 'classroomId', label: 'Attached room', type: 'select',
      optionsFrom: { endpoint: '/academics/classrooms', labelKey: 'name' },
      hint: 'Links movement here to a teaching room so lessons can be matched.',
    },
    { name: 'building', label: 'Building', type: 'text' },
    { name: 'floor', label: 'Floor', type: 'text' },
    {
      name: 'isCampusBoundary', label: 'This is a campus boundary', type: 'checkbox',
      hint: 'Crossing here moves a student on or off site and triggers arrival notifications.',
      fullWidth: true,
    },
    {
      name: 'notifyGuardians', label: 'Notify parents about movement here', type: 'checkbox',
      fullWidth: true,
    },
    {
      name: 'affectsAttendance', label: 'Count towards attendance', type: 'checkbox',
      defaultValue: true, fullWidth: true,
    },
    {
      name: 'mapX', label: 'Map position X', type: 'number', min: 0, max: 1, step: 0.01,
      hint: '0 to 1 across the floor plan. Needed to place the reader on the live map.',
    },
    { name: 'mapY', label: 'Map position Y', type: 'number', min: 0, max: 1, step: 0.01 },
  ],
  transformSubmit: (values) => ({
    ...values,
    classroomId: values.classroomId ? Number(values.classroomId) : undefined,
    mapX: values.mapX === '' ? undefined : Number(values.mapX),
    mapY: values.mapY === '' ? undefined : Number(values.mapY),
  }),
  emptyMessage: 'Define the gates and doors you want monitored before adding readers.',
};

export const cardsResource: ResourceConfig = {
  title: 'RFID cards',
  singular: 'card',
  endpoint: '/rfid/tags',
  searchPlaceholder: 'Search by EPC, card number or student',
  description: 'Physical tags and who holds them',
  permissions: { view: P.rfidTags },
  filters: [
    {
      name: 'status', label: 'All statuses',
      options: [
        { value: 'Active', label: 'Active' },
        { value: 'Unassigned', label: 'Unassigned' },
        { value: 'Lost', label: 'Lost' },
        { value: 'Damaged', label: 'Damaged' },
        { value: 'Revoked', label: 'Revoked' },
        { value: 'Replaced', label: 'Replaced' },
      ],
    },
  ],
  columns: [
    {
      key: 'epc',
      header: 'Card',
      render: (row) => (
        <div className="event-body">
          {/* The full EPC is shown only here, where the permission to manage cards is
              already required, because matching a card to its printout needs it. */}
          <strong className="mono">{String(row.epc)}</strong>
          {row.cardNumber ? <span className="muted">Card {String(row.cardNumber)}</span> : null}
        </div>
      ),
    },
    {
      key: 'studentName',
      header: 'Held by',
      render: (row) => {
        if (row.studentName) {
          return (
            <div className="event-body">
              <strong>{String(row.studentName)}</strong>
              <span className="mono">{String(row.studentCode ?? '')}</span>
            </div>
          );
        }
        if (row.teacherName) return <span>{String(row.teacherName)}</span>;
        return <span className="muted">Unassigned</span>;
      },
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const status = String(row.status);
        const tone = status === 'Active' ? 'success'
          : status === 'Unassigned' ? 'warning'
          : 'danger';
        return <Badge tone={tone}>{status}</Badge>;
      },
    },
    { key: 'lastSeenLocation', header: 'Last seen at', hideBelow: 'md' },
    { key: 'lastSeenAtUtc', header: 'Last seen', hideBelow: 'lg' },
  ],
  headerActions: ({ refresh }) => <AssignCardButton onDone={refresh} />,
  /** Only a card that is actually in use can be revoked. */
  rowActions: (row, { refresh }) =>
    row.status === 'Active'
      ? <RevokeCardButton tagId={Number(row.id)} epc={String(row.epc)} onDone={refresh} />
      : null,
  emptyMessage: 'Assign a card to a student from their record, or with the Assign card button above.',
};

export const eventsLogResource: ResourceConfig = {
  title: 'Movement log',
  singular: 'movement',
  endpoint: '/rfid/events',
  searchPlaceholder: 'Search by student or location',
  description: 'Every resolved gate and room movement',
  permissions: { view: P.rfidEvents },
  filters: [
    {
      name: 'eventType', label: 'All movements',
      options: [
        { value: 'SchoolEntry', label: 'Arrived at school' },
        { value: 'SchoolExit', label: 'Left school' },
        { value: 'ClassroomEntry', label: 'Entered classroom' },
        { value: 'ClassroomExit', label: 'Left classroom' },
        { value: 'ZoneEntry', label: 'Entered area' },
        { value: 'ZoneExit', label: 'Left area' },
        { value: 'UnknownTag', label: 'Unrecognised card' },
      ],
    },
    {
      name: 'locationId', label: 'All locations',
      optionsFrom: { endpoint: '/rfid/locations', labelKey: 'name' },
    },
    { name: 'fromDate', label: 'From', type: 'date' },
    { name: 'toDate', label: 'To', type: 'date' },
    {
      name: 'includeRejected', label: 'Rejected reads',
      options: [{ value: 'true', label: 'Include rejected reads' }],
    },
  ],
  columns: [
    {
      key: 'occurredAtUtc',
      header: 'When',
      render: (row) => (
        <span className="tabular">
          {new Date(String(row.occurredAtUtc)).toLocaleString([], {
            day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit',
          })}
        </span>
      ),
    },
    {
      key: 'studentName',
      header: 'Who',
      render: (row) =>
        row.studentName ? (
          <div className="event-body">
            <strong>{String(row.studentName)}</strong>
            <span className="mono">{String(row.studentCode ?? '')}</span>
          </div>
        ) : (
          <div className="event-body">
            <strong className="muted">Unrecognised card</strong>
            <span className="mono">{String(row.maskedEpc ?? '')}</span>
          </div>
        ),
    },
    {
      key: 'eventTypeName',
      header: 'Movement',
      render: (row) => {
        const type = String(row.eventTypeName);
        const isEntry = type.includes('Entry');
        const rejected = type === 'UnknownTag' || type === 'Rejected';

        return (
          <Badge tone={rejected ? 'danger' : isEntry ? 'success' : 'neutral'}>
            <Icon name={rejected ? 'alert' : isEntry ? 'login' : 'logout'} size={11} />
            {describeMovement(type)}
          </Badge>
        );
      },
    },
    { key: 'locationName', header: 'Where' },
    { key: 'subjectName', header: 'Lesson', hideBelow: 'md' },
    {
      key: 'confidence',
      header: 'Confidence',
      align: 'right',
      hideBelow: 'lg',
      render: (row) => {
        const confidence = Number(row.confidence ?? 0);
        if (confidence === 0) return <span className="muted">—</span>;

        // Confidence below 1.0 means direction was inferred rather than observed; worth
        // showing when someone is investigating a disputed record.
        return (
          <span className={confidence < 0.8 ? 'muted' : 'tabular'}>
            {Math.round(confidence * 100)}%
          </span>
        );
      },
    },
  ],
  exportPath: '/api/v1/reports/rfid-movements?format=xlsx',
  emptyMessage: 'Movements appear here as soon as a card is read at any monitored door.',
};

function describeMovement(type: string) {
  switch (type) {
    case 'SchoolEntry': return 'Arrived';
    case 'SchoolExit': return 'Left school';
    case 'ClassroomEntry': return 'Entered room';
    case 'ClassroomExit': return 'Left room';
    case 'ZoneEntry': return 'Entered';
    case 'ZoneExit': return 'Left';
    case 'UnknownTag': return 'Unknown card';
    case 'Rejected': return 'Rejected';
    default: return type;
  }
}

function humanise(value: string) {
  return value.replace(/([A-Z])/g, ' $1').trim();
}
