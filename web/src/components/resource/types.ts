import type { ReactNode } from 'react';

/**
 * Declarative description of a manageable resource.
 *
 * Every management screen in the portal is one of these objects. The alternative — writing a
 * bespoke table, form, filter bar and delete dialog per module — produces twenty screens that
 * drift apart: one forgets a loading state, another shows a raw error, a third paginates
 * differently. Describing the resource once and rendering it through shared components means
 * a fix to empty states or validation lands everywhere at once.
 *
 * Nothing here holds data. Every option that needs values (a class list, a teacher list)
 * declares where to fetch them, so the screens stay driven by whatever the school has
 * actually configured.
 */
export interface ResourceConfig<T = Record<string, unknown>> {
  /** Plural title shown as the page heading, e.g. "Teachers". */
  title: string;
  /** Singular noun used in buttons and dialogs, e.g. "teacher". */
  singular: string;
  /** Base API path, e.g. "/teachers". */
  endpoint: string;

  /** Overrides when create/update/delete do not follow the REST default. */
  createEndpoint?: string;
  updateEndpoint?: (row: T) => string;
  deleteEndpoint?: (row: T) => string;

  /** Query key prefix for the cache. Defaults to the endpoint. */
  queryKey?: string;

  /** Stable row identity. Defaults to `row.id`. */
  rowId?: (row: T) => string | number;

  columns: ColumnConfig<T>[];
  /** Fields shown in the create/edit form. Omit to make the resource read-only. */
  fields?: FieldConfig[];
  filters?: FilterConfig[];

  searchPlaceholder?: string;
  /** Set false for endpoints that return a plain array rather than a page envelope. */
  paged?: boolean;
  defaultPageSize?: number;

  /** Permissions gating each operation. Absent means the operation is unavailable. */
  permissions?: {
    view?: string;
    create?: string;
    edit?: string;
    delete?: string;
  };

  /** Extra buttons in the page header. */
  headerActions?: (context: ResourceContext<T>) => ReactNode;
  /** Extra per-row buttons, shown before the standard edit/delete. */
  rowActions?: (row: T, context: ResourceContext<T>) => ReactNode;

  /** Renders an expandable detail panel under a row. */
  expandedRow?: (row: T) => ReactNode;

  /** Shape the payload before it is sent, e.g. to coerce numbers or drop empties. */
  transformSubmit?: (values: Record<string, unknown>, mode: 'create' | 'edit') => Record<string, unknown>;
  /** Map a row onto form values when opening the edit dialog. */
  toFormValues?: (row: T) => Record<string, unknown>;

  /** Static query parameters always sent with the list request. */
  listParams?: Record<string, unknown>;

  /** Report path used by the export button. Omitted means no export. */
  exportPath?: string;

  emptyMessage?: string;
  /** Shown above the table — useful for explaining what a screen is for. */
  description?: string;
}

export interface ResourceContext<T> {
  refresh: () => void;
  openCreate: () => void;
  openEdit: (row: T) => void;
  rows: T[];
}

export interface ColumnConfig<T = Record<string, unknown>> {
  key: string;
  header: string;
  /** Custom cell rendering. Falls back to a type-aware default. */
  render?: (row: T) => ReactNode;
  /** Sort key sent to the API. Absent means the column is not sortable. */
  sortKey?: string;
  width?: string;
  align?: 'left' | 'right' | 'center';
  /** Hide below this breakpoint so a phone shows only what matters. */
  hideBelow?: 'sm' | 'md' | 'lg';
}

export type FieldType =
  | 'text'
  | 'textarea'
  | 'number'
  | 'email'
  | 'tel'
  | 'password'
  | 'date'
  | 'time'
  | 'datetime'
  | 'select'
  | 'multiselect'
  | 'checkbox'
  | 'colour'
  | 'hex';

export interface FieldConfig {
  name: string;
  label: string;
  type: FieldType;
  required?: boolean;
  placeholder?: string;
  hint?: string;
  /** Static choices for a select. */
  options?: Array<{ value: string | number; label: string }>;
  /**
   * Fetches choices from the API. This is what keeps forms dynamic: a section dropdown
   * lists the sections the school actually has, not a hardcoded list.
   */
  optionsFrom?: {
    endpoint: string;
    valueKey?: string;
    labelKey?: string | ((row: Record<string, unknown>) => string);
    params?: Record<string, unknown>;
  };
  /** Only show this field when the predicate passes — used for dependent fields. */
  showWhen?: (values: Record<string, unknown>) => boolean;
  /** Return a message to block submission. */
  validate?: (value: unknown, values: Record<string, unknown>) => string | null;
  defaultValue?: unknown;
  /** Full width in the two-column form grid. */
  fullWidth?: boolean;
  /** Shown on create but not edit — e.g. an initial password. */
  createOnly?: boolean;
  /** Not editable after creation — e.g. a generated code. */
  readOnlyOnEdit?: boolean;
  min?: number;
  max?: number;
  step?: number;
  rows?: number;
}

export interface FilterConfig {
  name: string;
  label: string;
  type?: 'select' | 'date' | 'boolean';
  options?: Array<{ value: string; label: string }>;
  optionsFrom?: {
    endpoint: string;
    valueKey?: string;
    labelKey?: string;
  };
  /** Applied when the filter is left blank. */
  defaultValue?: string;
}
