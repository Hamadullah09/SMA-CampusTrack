import { Badge, Icon } from '@/components/ui';
import { P } from '@/lib/auth';
import type { ResourceConfig } from '@/components/resource/types';

/**
 * Resource definitions for people.
 *
 * Every dropdown here loads from the API, so the forms present whatever the school has
 * actually configured rather than a fixed list baked into the build.
 */

const PERSON_STATUS = [
  { value: 'Active', label: 'Active' },
  { value: 'Pending', label: 'Pending' },
  { value: 'Suspended', label: 'Suspended' },
  { value: 'Inactive', label: 'Inactive' },
];

const GENDERS = [
  { value: 'Unspecified', label: 'Prefer not to say' },
  { value: 'Male', label: 'Male' },
  { value: 'Female', label: 'Female' },
  { value: 'Other', label: 'Other' },
];

export const teachersResource: ResourceConfig = {
  title: 'Teachers',
  singular: 'teacher',
  endpoint: '/teachers',
  searchPlaceholder: 'Search by name, code or specialism',
  description: 'Staff who teach classes',
  permissions: {
    view: P.teachersView,
    create: P.teachersCreate,
    edit: 'teachers.edit',
    delete: 'teachers.delete',
  },
  filters: [
    { name: 'status', label: 'All statuses', options: PERSON_STATUS },
  ],
  columns: [
    {
      key: 'fullName',
      header: 'Teacher',
      sortKey: 'name',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.fullName ?? '')}</strong>
          <span className="mono">{String(row.teacherCode ?? '')}</span>
        </div>
      ),
    },
    {
      key: 'subjects',
      header: 'Subjects',
      hideBelow: 'md',
      render: (row) => {
        const subjects = (row.subjects as string[]) ?? [];
        if (subjects.length === 0) return <span className="muted">Not assigned</span>;

        return (
          <div className="row wrap" style={{ gap: 4 }}>
            {subjects.slice(0, 3).map((subject) => (
              <Badge key={subject} tone="brand">{subject}</Badge>
            ))}
            {subjects.length > 3 && <span className="muted">+{subjects.length - 3}</span>}
          </div>
        );
      },
    },
    {
      key: 'sectionCount',
      header: 'Classes',
      align: 'right',
      hideBelow: 'sm',
      render: (row) => <span className="tabular">{String(row.sectionCount ?? 0)}</span>,
    },
    { key: 'email', header: 'Email', hideBelow: 'lg' },
    { key: 'phoneNumber', header: 'Phone', hideBelow: 'lg' },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Active' ? 'success' : 'neutral'}>{String(row.status)}</Badge>
      ),
    },
  ],
  fields: [
    { name: 'firstName', label: 'First name', type: 'text', required: true },
    { name: 'lastName', label: 'Last name', type: 'text', required: true },
    {
      name: 'teacherCode',
      label: 'Teacher code',
      type: 'text',
      hint: 'Leave blank to generate the next code automatically.',
      readOnlyOnEdit: true,
    },
    { name: 'email', label: 'Email', type: 'email' },
    { name: 'phoneNumber', label: 'Phone', type: 'tel' },
    { name: 'gender', label: 'Gender', type: 'select', options: GENDERS, defaultValue: 'Unspecified' },
    { name: 'dateOfBirth', label: 'Date of birth', type: 'date' },
    { name: 'hireDate', label: 'Hire date', type: 'date' },
    { name: 'qualification', label: 'Qualification', type: 'text' },
    { name: 'specialisation', label: 'Specialism', type: 'text' },
    { name: 'officeLocation', label: 'Office', type: 'text' },
    { name: 'address', label: 'Address', type: 'text', fullWidth: true },
    { name: 'status', label: 'Status', type: 'select', options: PERSON_STATUS, defaultValue: 'Active' },
    {
      name: 'password',
      label: 'Initial password',
      type: 'password',
      createOnly: true,
      hint: 'Leave blank and a temporary password is generated and shown once.',
      fullWidth: true,
    },
  ],
  emptyMessage: 'Add teachers so they can be assigned to classes and take registers.',
};

export const guardiansResource: ResourceConfig = {
  title: 'Parents and guardians',
  singular: 'parent',
  endpoint: '/guardians',
  searchPlaceholder: 'Search by parent or child name',
  description: 'Each may follow several children',
  permissions: {
    view: P.guardiansView,
    create: P.guardiansCreate,
    edit: 'guardians.edit',
    delete: 'guardians.delete',
  },
  columns: [
    {
      key: 'fullName',
      header: 'Parent',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.fullName ?? '')}</strong>
          <span className="mono">{String(row.guardianCode ?? '')}</span>
        </div>
      ),
    },
    {
      key: 'children',
      header: 'Children',
      render: (row) => {
        const children = (row.children as string[]) ?? [];
        if (children.length === 0) return <span className="muted">None linked</span>;

        return (
          <div className="row wrap" style={{ gap: 4 }}>
            {children.map((child) => <Badge key={child} tone="info">{child}</Badge>)}
          </div>
        );
      },
    },
    { key: 'phoneNumber', header: 'Phone', hideBelow: 'md' },
    { key: 'email', header: 'Email', hideBelow: 'lg' },
    {
      key: 'hasPendingLinks',
      header: 'Access',
      render: (row) =>
        row.hasPendingLinks ? (
          // Worth surfacing: until a link is approved the parent sees nothing at all.
          <Badge tone="warning" title="A child link is waiting for approval">
            <Icon name="alert" size={11} /> Approval needed
          </Badge>
        ) : (
          <Badge tone="success">Active</Badge>
        ),
    },
  ],
  fields: [
    { name: 'firstName', label: 'First name', type: 'text', required: true },
    { name: 'lastName', label: 'Last name', type: 'text', required: true },
    { name: 'email', label: 'Email', type: 'email' },
    { name: 'phoneNumber', label: 'Phone', type: 'tel', required: true },
    { name: 'alternatePhone', label: 'Alternative phone', type: 'tel' },
    { name: 'occupation', label: 'Occupation', type: 'text' },
    { name: 'gender', label: 'Gender', type: 'select', options: GENDERS, defaultValue: 'Unspecified' },
    { name: 'address', label: 'Address', type: 'text', fullWidth: true },
    {
      name: 'password',
      label: 'Initial password',
      type: 'password',
      createOnly: true,
      hint: 'Leave blank and a temporary password is generated and shown once.',
      fullWidth: true,
    },
  ],
  emptyMessage: 'Add parents so they receive arrival notifications and can follow their child.',
};

export const staffResource: ResourceConfig = {
  title: 'Staff',
  singular: 'staff member',
  endpoint: '/staff',
  searchPlaceholder: 'Search by name or role',
  description: 'Non-teaching staff',
  permissions: {
    view: 'staff.view',
    create: 'staff.create',
    edit: 'staff.edit',
    delete: 'staff.delete',
  },
  columns: [
    {
      key: 'fullName',
      header: 'Name',
      render: (row) => (
        <div className="event-body">
          <strong>{String(row.fullName ?? '')}</strong>
          <span className="mono">{String(row.staffCode ?? '')}</span>
        </div>
      ),
    },
    { key: 'jobTitle', header: 'Role' },
    { key: 'department', header: 'Department', hideBelow: 'md' },
    { key: 'phoneNumber', header: 'Phone', hideBelow: 'lg' },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge tone={row.status === 'Active' ? 'success' : 'neutral'}>{String(row.status)}</Badge>
      ),
    },
  ],
  fields: [
    { name: 'firstName', label: 'First name', type: 'text', required: true },
    { name: 'lastName', label: 'Last name', type: 'text', required: true },
    { name: 'jobTitle', label: 'Job title', type: 'text', required: true },
    { name: 'department', label: 'Department', type: 'text' },
    { name: 'email', label: 'Email', type: 'email' },
    { name: 'phoneNumber', label: 'Phone', type: 'tel' },
    { name: 'hireDate', label: 'Hire date', type: 'date' },
    { name: 'status', label: 'Status', type: 'select', options: PERSON_STATUS, defaultValue: 'Active' },
    {
      name: 'password', label: 'Initial password', type: 'password', createOnly: true,
      hint: 'Leave blank to generate one.', fullWidth: true,
    },
  ],
};
