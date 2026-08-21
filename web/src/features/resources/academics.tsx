import { Badge } from '@/components/ui';
import { P } from '@/lib/auth';
import type { ResourceConfig } from '@/components/resource/types';

/** Academic structure: sessions, classes, sections, subjects, courses and rooms. */

export const sessionsResource: ResourceConfig = {
  title: 'Academic sessions',
  singular: 'session',
  endpoint: '/academics/sessions',
  paged: false,
  description: 'Exactly one is current at a time',
  permissions: {
    view: 'academics.sessions.view', create: P.sessionsManage,
    edit: P.sessionsManage, delete: P.sessionsManage,
  },
  columns: [
    {
      key: 'name',
      header: 'Session',
      render: (row) => (
        <div className="row">
          <strong>{String(row.name)}</strong>
          {Boolean(row.isCurrent) && <Badge tone="success" dot>Current</Badge>}
        </div>
      ),
    },
    { key: 'code', header: 'Code', render: (row) => <span className="mono">{String(row.code)}</span> },
    { key: 'startDate', header: 'Starts' },
    { key: 'endDate', header: 'Ends' },
    {
      key: 'studentCount',
      header: 'Students',
      align: 'right',
      render: (row) => <span className="tabular">{String(row.studentCount ?? 0)}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Active' ? 'success' : 'neutral'}>{String(row.status)}</Badge>
      ),
    },
  ],
  fields: [
    { name: 'name', label: 'Name', type: 'text', required: true, placeholder: '2026/2027' },
    { name: 'code', label: 'Code', type: 'text', required: true, placeholder: 'AY2627' },
    { name: 'startDate', label: 'Start date', type: 'date', required: true },
    { name: 'endDate', label: 'End date', type: 'date', required: true },
    {
      name: 'termType', label: 'Term structure', type: 'select', defaultValue: 'FullYear',
      options: [
        { value: 'FullYear', label: 'Full year' },
        { value: 'Semester', label: 'Semesters' },
        { value: 'Trimester', label: 'Trimesters' },
        { value: 'Quarter', label: 'Quarters' },
      ],
    },
    {
      name: 'isCurrent', label: 'Make this the current session', type: 'checkbox',
      hint: 'New enrolments, timetables and attendance attach to the current session.',
      fullWidth: true,
    },
  ],
  emptyMessage: 'Create a session before adding classes, timetables or students.',
};

export const classesResource: ResourceConfig = {
  title: 'Classes and sections',
  singular: 'class',
  endpoint: '/academics/classes',
  paged: false,
  description: 'Year groups and the sections within them',
  permissions: { view: P.classesView, create: P.classesManage, edit: P.classesManage, delete: P.classesManage },
  columns: [
    { key: 'name', header: 'Class', render: (row) => <strong>{String(row.name)}</strong> },
    { key: 'code', header: 'Code', render: (row) => <span className="mono">{String(row.code)}</span> },
    { key: 'level', header: 'Level', align: 'right', hideBelow: 'sm' },
    {
      key: 'sectionCount',
      header: 'Sections',
      align: 'right',
      render: (row) => <span className="tabular">{String(row.sectionCount ?? 0)}</span>,
    },
    {
      key: 'studentCount',
      header: 'Students',
      align: 'right',
      render: (row) => <span className="tabular">{String(row.studentCount ?? 0)}</span>,
    },
  ],
  // Sections belong to a class, so they are shown by expanding it rather than on a
  // separate screen the user has to correlate by eye.
  expandedRow: (row) => {
    const sections = (row.sections as Array<Record<string, unknown>>) ?? [];

    if (sections.length === 0) {
      return <p className="muted">No sections yet. Add one from the Sections screen.</p>;
    }

    return (
      <div>
        <p className="label" style={{ marginBottom: 'var(--space-2)' }}>Sections</p>
        <div className="row wrap">
          {sections.map((section) => (
            <div
              key={String(section.id)}
              className="card"
              style={{ padding: 'var(--space-3)', minWidth: 190 }}
            >
              <strong>{String(section.displayName)}</strong>
              <div className="muted" style={{ fontSize: 'var(--text-sm)' }}>
                {String(section.studentCount ?? 0)} of {String(section.capacity)} places
              </div>
              <div className="muted" style={{ fontSize: 'var(--text-sm)' }}>
                {section.homeroomTeacher ? String(section.homeroomTeacher) : 'No form teacher'}
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  },
  fields: [
    { name: 'name', label: 'Class name', type: 'text', required: true, placeholder: 'Grade 7' },
    { name: 'code', label: 'Code', type: 'text', required: true, placeholder: 'G7' },
    {
      name: 'level', label: 'Year level', type: 'number', required: true, min: 1, max: 20,
      hint: 'Used to order classes and to drive progression.',
    },
    {
      name: 'courseId', label: 'Programme', type: 'select',
      optionsFrom: { endpoint: '/academics/courses', labelKey: 'name' },
      hint: 'Optional. Links this class to a course of study.',
    },
  ],
};

export const sectionsResource: ResourceConfig = {
  title: 'Sections',
  singular: 'section',
  endpoint: '/academics/sections',
  paged: false,
  description: 'Teaching groups within a class',
  permissions: {
    view: 'academics.sections.view',
    create: 'academics.sections.manage',
    edit: 'academics.sections.manage',
    delete: 'academics.sections.manage',
  },
  filters: [
    {
      name: 'classId', label: 'All classes',
      optionsFrom: { endpoint: '/academics/classes', labelKey: 'name' },
    },
  ],
  columns: [
    { key: 'displayName', header: 'Section', render: (row) => <strong>{String(row.displayName)}</strong> },
    { key: 'className', header: 'Class', hideBelow: 'sm' },
    {
      key: 'studentCount',
      header: 'Students',
      align: 'right',
      render: (row) => {
        const count = Number(row.studentCount ?? 0);
        const capacity = Number(row.capacity ?? 0);
        const full = capacity > 0 && count >= capacity;

        return (
          <span className={`tabular ${full ? '' : ''}`}>
            {count} / {capacity}
            {/* Capacity is a real constraint: enrolment refuses to overfill a section. */}
            {full && <Badge tone="warning">&nbsp;Full</Badge>}
          </span>
        );
      },
    },
    { key: 'homeroomTeacher', header: 'Form teacher', hideBelow: 'md' },
    { key: 'classroomName', header: 'Default room', hideBelow: 'lg' },
  ],
  fields: [
    {
      name: 'schoolClassId', label: 'Class', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/classes', labelKey: 'name' },
      readOnlyOnEdit: true,
    },
    { name: 'name', label: 'Section name', type: 'text', required: true, placeholder: 'A' },
    { name: 'capacity', label: 'Capacity', type: 'number', required: true, min: 1, max: 200, defaultValue: 40 },
    {
      name: 'homeroomTeacherId', label: 'Form teacher', type: 'select',
      optionsFrom: { endpoint: '/teachers', labelKey: 'fullName', params: { pageSize: 200 } },
    },
    {
      name: 'defaultClassroomId', label: 'Default room', type: 'select',
      optionsFrom: { endpoint: '/academics/classrooms', labelKey: 'name' },
    },
  ],
  transformSubmit: (values) => ({
    ...values,
    schoolClassId: Number(values.schoolClassId),
    capacity: Number(values.capacity),
    homeroomTeacherId: values.homeroomTeacherId ? Number(values.homeroomTeacherId) : undefined,
    defaultClassroomId: values.defaultClassroomId ? Number(values.defaultClassroomId) : undefined,
  }),
};

export const subjectsResource: ResourceConfig = {
  title: 'Subjects',
  singular: 'subject',
  endpoint: '/academics/subjects',
  paged: false,
  permissions: {
    view: P.subjectsView, create: P.subjectsManage,
    edit: P.subjectsManage, delete: P.subjectsManage,
  },
  columns: [
    {
      key: 'name',
      header: 'Subject',
      render: (row) => (
        <div className="row">
          {/* The colour is reused on the timetable and in the mobile app, so it is shown
              here where it is chosen. */}
          <span
            style={{
              width: 10, height: 10, borderRadius: 3, flexShrink: 0,
              background: (row.colourHex as string) ?? 'var(--slate-400)',
            }}
          />
          <strong>{String(row.name)}</strong>
        </div>
      ),
    },
    { key: 'code', header: 'Code', render: (row) => <span className="mono">{String(row.code)}</span> },
    { key: 'credits', header: 'Credits', align: 'right', hideBelow: 'sm' },
    {
      key: 'totalPlannedClasses', header: 'Planned lessons', align: 'right', hideBelow: 'md',
      render: (row) => <span className="tabular">{String(row.totalPlannedClasses ?? 0)}</span>,
    },
    {
      key: 'teacherCount', header: 'Teachers', align: 'right', hideBelow: 'md',
      render: (row) => <span className="tabular">{String(row.teacherCount ?? 0)}</span>,
    },
    {
      key: 'isElective', header: 'Type',
      render: (row) => (
        <Badge tone={row.isElective ? 'info' : 'neutral'}>
          {row.isElective ? 'Elective' : 'Core'}
        </Badge>
      ),
    },
  ],
  fields: [
    { name: 'name', label: 'Subject name', type: 'text', required: true },
    { name: 'code', label: 'Code', type: 'text', required: true, placeholder: 'SCI' },
    { name: 'credits', label: 'Credits', type: 'number', min: 0, max: 20, defaultValue: 1 },
    {
      name: 'totalPlannedClasses', label: 'Planned lessons per year', type: 'number', min: 0,
      hint: 'The denominator for subject attendance percentages.',
    },
    { name: 'colourHex', label: 'Colour', type: 'colour', defaultValue: '#4f46e5' },
    { name: 'isElective', label: 'Elective subject', type: 'checkbox' },
    { name: 'description', label: 'Description', type: 'textarea', fullWidth: true },
  ],
  transformSubmit: (values) => ({
    ...values,
    credits: Number(values.credits || 1),
    totalPlannedClasses: Number(values.totalPlannedClasses || 0),
  }),
};

export const coursesResource: ResourceConfig = {
  title: 'Courses',
  singular: 'course',
  endpoint: '/academics/courses',
  paged: false,
  description: 'Programmes of study spanning several years',
  permissions: {
    view: 'academics.courses.view', create: 'academics.courses.manage',
    edit: 'academics.courses.manage', delete: 'academics.courses.manage',
  },
  columns: [
    { key: 'name', header: 'Course', render: (row) => <strong>{String(row.name)}</strong> },
    { key: 'code', header: 'Code', render: (row) => <span className="mono">{String(row.code)}</span> },
    { key: 'durationYears', header: 'Years', align: 'right' },
    {
      key: 'classCount', header: 'Classes', align: 'right', hideBelow: 'sm',
      render: (row) => <span className="tabular">{String(row.classCount ?? 0)}</span>,
    },
    {
      key: 'subjects', header: 'Subjects', hideBelow: 'md',
      render: (row) => {
        const subjects = (row.subjects as Array<Record<string, unknown>>) ?? [];
        if (subjects.length === 0) return <span className="muted">None</span>;

        return (
          <div className="row wrap" style={{ gap: 4 }}>
            {subjects.slice(0, 3).map((s) => (
              <Badge key={String(s.subjectId)} tone="brand">{String(s.name)}</Badge>
            ))}
            {subjects.length > 3 && <span className="muted">+{subjects.length - 3}</span>}
          </div>
        );
      },
    },
  ],
  fields: [
    { name: 'name', label: 'Course name', type: 'text', required: true },
    { name: 'code', label: 'Code', type: 'text', required: true },
    { name: 'durationYears', label: 'Duration in years', type: 'number', min: 1, max: 10, defaultValue: 1 },
    { name: 'description', label: 'Description', type: 'textarea', fullWidth: true },
  ],
  transformSubmit: (values) => ({ ...values, durationYears: Number(values.durationYears || 1) }),
};

export const classroomsResource: ResourceConfig = {
  title: 'Rooms',
  singular: 'room',
  endpoint: '/academics/classrooms',
  paged: false,
  description: 'Teaching spaces, and which are RFID-monitored',
  permissions: {
    view: P.classroomsView, create: P.classroomsManage,
    edit: P.classroomsManage, delete: P.classroomsManage,
  },
  columns: [
    { key: 'name', header: 'Room', render: (row) => <strong>{String(row.name)}</strong> },
    { key: 'code', header: 'Code', render: (row) => <span className="mono">{String(row.code)}</span> },
    { key: 'building', header: 'Building', hideBelow: 'sm' },
    { key: 'floor', header: 'Floor', hideBelow: 'md' },
    { key: 'capacity', header: 'Capacity', align: 'right', hideBelow: 'sm' },
    { key: 'roomType', header: 'Type', hideBelow: 'md' },
    {
      key: 'isMonitored',
      header: 'RFID',
      render: (row) =>
        row.isMonitored ? (
          <Badge tone="success" dot title="A reader location is attached to this room">Monitored</Badge>
        ) : (
          <Badge tone="neutral">Not monitored</Badge>
        ),
    },
  ],
  fields: [
    { name: 'name', label: 'Room name', type: 'text', required: true },
    { name: 'code', label: 'Code', type: 'text', required: true },
    { name: 'building', label: 'Building', type: 'text' },
    { name: 'floor', label: 'Floor', type: 'text' },
    { name: 'capacity', label: 'Capacity', type: 'number', min: 1, defaultValue: 40 },
    {
      name: 'roomType', label: 'Room type', type: 'select',
      options: [
        { value: 'Lecture', label: 'Lecture room' },
        { value: 'Lab', label: 'Laboratory' },
        { value: 'Computer', label: 'Computer room' },
        { value: 'Workshop', label: 'Workshop' },
        { value: 'Hall', label: 'Hall' },
      ],
    },
    { name: 'hasProjector', label: 'Has a projector', type: 'checkbox' },
    {
      name: 'mapX', label: 'Map position X', type: 'number', min: 0, max: 1, step: 0.01,
      hint: '0 to 1, measured from the left edge of the floor plan.',
    },
    {
      name: 'mapY', label: 'Map position Y', type: 'number', min: 0, max: 1, step: 0.01,
      hint: '0 to 1, measured from the top of the floor plan.',
    },
  ],
  transformSubmit: (values) => ({
    ...values,
    capacity: Number(values.capacity || 40),
    mapX: values.mapX === '' ? undefined : Number(values.mapX),
    mapY: values.mapY === '' ? undefined : Number(values.mapY),
  }),
};

export const teachingAssignmentsResource: ResourceConfig = {
  title: 'Teaching assignments',
  singular: 'assignment',
  endpoint: '/academics/teaching-assignments',
  paged: false,
  description: 'Who teaches which subject to which section',
  permissions: {
    view: 'academics.sections.view',
    create: 'academics.assignments.manage',
    edit: 'academics.assignments.manage',
    delete: 'academics.assignments.manage',
  },
  columns: [
    { key: 'teacherName', header: 'Teacher', render: (row) => <strong>{String(row.teacherName)}</strong> },
    { key: 'subjectName', header: 'Subject' },
    { key: 'sectionName', header: 'Section' },
    {
      key: 'isPrimary', header: 'Role',
      render: (row) => (
        <Badge tone={row.isPrimary ? 'brand' : 'neutral'}>
          {row.isPrimary ? 'Lead' : 'Assisting'}
        </Badge>
      ),
    },
  ],
  fields: [
    {
      name: 'teacherId', label: 'Teacher', type: 'select', required: true,
      optionsFrom: { endpoint: '/teachers', labelKey: 'fullName', params: { pageSize: 200 } },
    },
    {
      name: 'subjectId', label: 'Subject', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/subjects', labelKey: 'name' },
    },
    {
      name: 'sectionId', label: 'Section', type: 'select', required: true,
      optionsFrom: { endpoint: '/academics/sections', labelKey: 'displayName' },
    },
    { name: 'isPrimary', label: 'Lead teacher for this subject', type: 'checkbox', defaultValue: true },
  ],
  transformSubmit: (values) => ({
    teacherId: Number(values.teacherId),
    subjectId: Number(values.subjectId),
    sectionId: Number(values.sectionId),
    isPrimary: Boolean(values.isPrimary),
  }),
  emptyMessage: 'Assign teachers to subjects and sections so their portal shows the right classes.',
};
