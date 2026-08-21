import { Fragment, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import { useAuth } from '@/lib/auth';
import {
  Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, Icon,
  LoadingState, Modal, Pagination, useDebounced, useToast,
} from '@/components/ui';
import { ResourceField, useResourceForm } from './ResourceForm';
import type { ColumnConfig, ResourceConfig } from './types';

type Row = Record<string, unknown>;

/**
 * Renders a complete management screen from a {@link ResourceConfig}.
 *
 * Search, filtering, sorting, pagination, create, edit, delete, permission gating, loading,
 * empty and error states are all handled here once. A module screen becomes a description of
 * its columns and fields rather than three hundred lines of near-duplicate JSX.
 */
export function ResourcePage<T extends Row = Row>({ config }: { config: ResourceConfig<T> }) {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string>>(() => {
    const seeded: Record<string, string> = {};
    for (const filter of config.filters ?? []) {
      if (filter.defaultValue) seeded[filter.name] = filter.defaultValue;
    }
    return seeded;
  });
  const [sort, setSort] = useState<{ by: string; desc: boolean } | null>(null);
  const [expanded, setExpanded] = useState<string | number | null>(null);

  const [editing, setEditing] = useState<{ mode: 'create' | 'edit'; row?: T } | null>(null);
  const [deleting, setDeleting] = useState<T | null>(null);

  const debouncedSearch = useDebounced(search);
  const queryKey = config.queryKey ?? config.endpoint;
  const rowId = config.rowId ?? ((row: T) => row.id as string | number);

  // Each operation must be granted explicitly. An undeclared permission means the API has
  // no endpoint for it, so offering the button would only produce a failed request --
  // treating "unspecified" as "allowed" is how a resource ends up with an Edit button it
  // cannot honour.
  const canCreate = Boolean(config.fields?.length)
    && Boolean(config.permissions?.create) && can(config.permissions!.create!);
  const canEdit = Boolean(config.fields?.length)
    && Boolean(config.permissions?.edit) && can(config.permissions!.edit!);
  const canDelete = Boolean(config.permissions?.delete) && can(config.permissions!.delete!);

  const listQuery = useQuery({
    queryKey: [queryKey, page, debouncedSearch, filters, sort, config.listParams],
    queryFn: async () => {
      const { data } = await api.get(config.endpoint, {
        params: {
          ...config.listParams,
          ...filters,
          page: config.paged === false ? undefined : page,
          pageSize: config.paged === false ? undefined : config.defaultPageSize ?? 25,
          search: debouncedSearch || undefined,
          sortBy: sort?.by,
          sortDescending: sort?.desc || undefined,
        },
      });

      // Endpoints return a page envelope, a bare array, or a report envelope. Normalise
      // all three so the table never has to care which.
      if (Array.isArray(data)) {
        return {
          items: data as T[], page: 1, pageSize: data.length,
          totalCount: data.length, totalPages: 1, hasPrevious: false, hasNext: false,
        } satisfies PagedResult<T>;
      }

      if ('items' in data && !('totalCount' in data)) {
        const items = (data.items ?? []) as T[];
        return {
          items, page: 1, pageSize: items.length,
          totalCount: items.length, totalPages: 1, hasPrevious: false, hasNext: false,
        } satisfies PagedResult<T>;
      }

      return data as PagedResult<T>;
    },
  });

  const rows = listQuery.data?.items ?? [];

  const save = useMutation({
    mutationFn: async ({ mode, row, payload }:
      { mode: 'create' | 'edit'; row?: T; payload: Record<string, unknown> }) => {
      const body = config.transformSubmit ? config.transformSubmit(payload, mode) : payload;

      if (mode === 'create') {
        return (await api.post(config.createEndpoint ?? config.endpoint, body)).data;
      }

      const path = config.updateEndpoint
        ? config.updateEndpoint(row!)
        : `${config.endpoint}/${rowId(row!)}`;

      return (await api.put(path, body)).data;
    },
    onSuccess: (_, variables) => {
      toast.success(
        variables.mode === 'create' ? `${capitalise(config.singular)} added` : 'Changes saved',
      );
      setEditing(null);
      void queryClient.invalidateQueries({ queryKey: [queryKey] });
    },
    onError: (error) => toast.error('Could not save', describeError(error)),
  });

  const remove = useMutation({
    mutationFn: async (row: T) => {
      const path = config.deleteEndpoint
        ? config.deleteEndpoint(row)
        : `${config.endpoint}/${rowId(row)}`;
      return api.delete(path);
    },
    onSuccess: () => {
      toast.success(`${capitalise(config.singular)} removed`);
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: [queryKey] });
    },
    onError: (error) => toast.error('Could not remove', describeError(error)),
  });

  const context = useMemo(() => ({
    refresh: () => void listQuery.refetch(),
    openCreate: () => setEditing({ mode: 'create' }),
    openEdit: (row: T) => setEditing({ mode: 'edit', row }),
    rows,
  }), [listQuery, rows]);

  function toggleSort(column: ColumnConfig<T>) {
    if (!column.sortKey) return;

    const key = column.sortKey;
    setSort((current) => {
      // Cycles ascending -> descending -> unsorted, which is what a repeated header click
      // is expected to do.
      if (current?.by !== key) return { by: key, desc: false };
      return current.desc ? null : { by: key, desc: true };
    });
    setPage(1);
  }

  const hasToolbar = Boolean(config.searchPlaceholder) || (config.filters?.length ?? 0) > 0;

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">{config.title}</h1>
          <p className="page-subtitle">
            {listQuery.data
              ? `${listQuery.data.totalCount.toLocaleString()} ${listQuery.data.totalCount === 1 ? config.singular : plural(config.singular)}`
              : 'Loading'}
            {config.description ? ` · ${config.description}` : ''}
          </p>
        </div>

        <div className="row wrap">
          {config.headerActions?.(context)}

          {config.exportPath && (
            <a className="btn btn-secondary" href={config.exportPath} target="_blank" rel="noreferrer">
              <Icon name="download" /> Export
            </a>
          )}

          <Button
            icon="refresh" aria-label="Refresh"
            onClick={() => void listQuery.refetch()} loading={listQuery.isFetching}
          />

          {canCreate && (
            <Button variant="primary" icon="plus" onClick={() => setEditing({ mode: 'create' })}>
              Add {config.singular}
            </Button>
          )}
        </div>
      </div>

      <Card flush>
        {hasToolbar && (
          <div className="toolbar">
            {config.searchPlaceholder && (
              <div className="search-box">
                <Icon name="search" size={15} />
                <input
                  className="input"
                  placeholder={config.searchPlaceholder}
                  value={search}
                  onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                  aria-label={`Search ${config.title}`}
                />
              </div>
            )}

            {config.filters?.map((filter) => (
              <FilterControl
                key={filter.name}
                filter={filter}
                value={filters[filter.name] ?? ''}
                onChange={(value) => {
                  setFilters((current) => ({ ...current, [filter.name]: value }));
                  setPage(1);
                }}
              />
            ))}

            <div className="grow" />

            {(debouncedSearch || Object.values(filters).some(Boolean)) && (
              <Button
                size="sm" variant="ghost"
                onClick={() => { setSearch(''); setFilters({}); setPage(1); }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {listQuery.isLoading ? (
          <LoadingState rows={8} />
        ) : listQuery.isError ? (
          <ErrorState
            message={describeError(listQuery.error)}
            onRetry={() => void listQuery.refetch()}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            title={
              debouncedSearch || Object.values(filters).some(Boolean)
                ? `No ${plural(config.singular)} match those filters`
                : `No ${plural(config.singular)} yet`
            }
            message={
              debouncedSearch || Object.values(filters).some(Boolean)
                ? 'Try a different search, or clear the filters.'
                : config.emptyMessage ?? `Add your first ${config.singular} to get started.`
            }
            action={
              canCreate && !debouncedSearch ? (
                <Button variant="primary" icon="plus" onClick={() => setEditing({ mode: 'create' })}>
                  Add {config.singular}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table-responsive">
                <thead>
                  <tr>
                    {config.expandedRow && <th style={{ width: 40 }} aria-label="Expand" />}
                    {config.columns.map((column) => (
                      <th
                        key={column.key}
                        className={`${column.sortKey ? 'sortable' : ''} ${column.hideBelow ? `hide-below-${column.hideBelow}` : ''}`}
                        style={{ width: column.width, textAlign: column.align }}
                        onClick={() => toggleSort(column)}
                        aria-sort={
                          sort !== null && sort.by === column.sortKey
                            ? (sort.desc ? 'descending' : 'ascending')
                            : undefined
                        }
                      >
                        <span className="row" style={{ gap: 4, justifyContent: column.align === 'right' ? 'flex-end' : undefined }}>
                          {column.header}
                          {column.sortKey && sort?.by === column.sortKey && (
                            <Icon name="chevronDown" size={12} />
                          )}
                        </span>
                      </th>
                    ))}
                    {(canEdit || canDelete || config.rowActions) && (
                      <th style={{ textAlign: 'right', width: 120 }}>Actions</th>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => {
                    const id = rowId(row);
                    const isExpanded = expanded === id;

                    return (
                      <Fragment key={String(id)}>
                        <tr>
                          {config.expandedRow && (
                            <td data-label="">
                              <Button
                                size="sm" variant="ghost"
                                icon={isExpanded ? 'chevronDown' : 'chevronRight'}
                                aria-label={isExpanded ? 'Collapse' : 'Expand'}
                                onClick={() => setExpanded(isExpanded ? null : id)}
                              />
                            </td>
                          )}

                          {config.columns.map((column) => (
                            <td
                              key={column.key}
                              data-label={column.header}
                              className={column.hideBelow ? `hide-below-${column.hideBelow}` : ''}
                              style={{ textAlign: column.align }}
                            >
                              {column.render ? column.render(row) : renderValue(row[column.key])}
                            </td>
                          ))}

                          {(canEdit || canDelete || config.rowActions) && (
                            <td data-label="Actions">
                              <div className="table-actions">
                                {config.rowActions?.(row, context)}
                                {canEdit && (
                                  <Button
                                    size="sm" variant="ghost" icon="edit"
                                    aria-label={`Edit ${config.singular}`}
                                    onClick={() => setEditing({ mode: 'edit', row })}
                                  />
                                )}
                                {canDelete && (
                                  <Button
                                    size="sm" variant="ghost" icon="trash"
                                    aria-label={`Remove ${config.singular}`}
                                    onClick={() => setDeleting(row)}
                                  />
                                )}
                              </div>
                            </td>
                          )}
                        </tr>

                        {isExpanded && config.expandedRow && (
                          <tr>
                            <td
                              colSpan={config.columns.length + 2}
                              style={{ background: 'var(--bg-sunken)', padding: 'var(--space-4)' }}
                            >
                              {config.expandedRow(row)}
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {config.paged !== false && listQuery.data && (
              <Pagination
                page={listQuery.data.page}
                pageSize={listQuery.data.pageSize}
                totalCount={listQuery.data.totalCount}
                totalPages={listQuery.data.totalPages}
                onPageChange={setPage}
              />
            )}
          </>
        )}
      </Card>

      {editing && config.fields && (
        <ResourceDialog
          config={config}
          mode={editing.mode}
          row={editing.row}
          saving={save.isPending}
          onCancel={() => setEditing(null)}
          onSubmit={(payload) => save.mutate({ mode: editing.mode, row: editing.row, payload })}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        title={`Remove this ${config.singular}?`}
        message={
          `This will remove the ${config.singular} from active lists. ` +
          'Historical records that reference it are preserved.'
        }
        confirmLabel="Remove"
        danger
        loading={remove.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
      />
    </>
  );
}

function ResourceDialog<T extends Row>({
  config, mode, row, saving, onCancel, onSubmit,
}: {
  config: ResourceConfig<T>;
  mode: 'create' | 'edit';
  row?: T;
  saving: boolean;
  onCancel: () => void;
  onSubmit: (payload: Record<string, unknown>) => void;
}) {
  const fields = config.fields ?? [];

  const initial = useMemo(() => {
    if (mode === 'create' || !row) return undefined;
    return config.toFormValues ? config.toFormValues(row) : (row as Record<string, unknown>);
  }, [config, mode, row]);

  const form = useResourceForm(fields, initial);

  const visible = fields.filter((field) => {
    if (field.createOnly && mode === 'edit') return false;
    if (field.showWhen && !field.showWhen(form.values)) return false;
    return true;
  });

  return (
    <Modal
      open
      onClose={onCancel}
      size="lg"
      title={mode === 'create' ? `Add ${config.singular}` : `Edit ${config.singular}`}
      footer={
        <>
          <Button onClick={onCancel} disabled={saving}>Cancel</Button>
          <Button
            variant="primary"
            loading={saving}
            onClick={() => {
              if (form.validate(mode)) onSubmit(form.payload(mode));
            }}
          >
            {mode === 'create' ? `Add ${config.singular}` : 'Save changes'}
          </Button>
        </>
      }
    >
      <div className="form-grid">
        {visible.map((field) => (
          <ResourceField
            key={field.name}
            field={field}
            value={form.values[field.name]}
            values={form.values}
            error={form.errors[field.name]}
            disabled={saving || (mode === 'edit' && field.readOnlyOnEdit)}
            onChange={(value) => form.setValue(field.name, value)}
          />
        ))}
      </div>
    </Modal>
  );
}

function FilterControl({
  filter, value, onChange,
}: {
  filter: NonNullable<ResourceConfig['filters']>[number];
  value: string;
  onChange: (value: string) => void;
}) {
  const remote = filter.optionsFrom;

  const optionsQuery = useQuery({
    queryKey: ['filter-options', remote?.endpoint],
    queryFn: async () => {
      const { data } = await api.get(remote!.endpoint);
      const rows = Array.isArray(data) ? data : (data.items ?? []);

      return (rows as Array<Record<string, unknown>>).map((r) => ({
        value: String(r[remote!.valueKey ?? 'id']),
        label: String(r[remote!.labelKey ?? 'name'] ?? ''),
      }));
    },
    enabled: Boolean(remote),
    staleTime: 60_000,
  });

  if (filter.type === 'date') {
    return (
      <input
        className="input" type="date" style={{ width: 'auto' }}
        value={value} onChange={(e) => onChange(e.target.value)}
        aria-label={filter.label}
      />
    );
  }

  const options = filter.options ?? optionsQuery.data ?? [];

  return (
    <select
      className="select" style={{ width: 'auto' }}
      value={value} onChange={(e) => onChange(e.target.value)}
      aria-label={filter.label}
    >
      <option value="">{filter.label}</option>
      {options.map((option) => (
        <option key={option.value} value={option.value}>{option.label}</option>
      ))}
    </select>
  );
}

/** Type-aware default rendering, so a column with no renderer still looks deliberate. */
function renderValue(value: unknown): React.ReactNode {
  if (value == null || value === '') return <span className="muted">—</span>;

  if (typeof value === 'boolean') {
    return <Badge tone={value ? 'success' : 'neutral'}>{value ? 'Yes' : 'No'}</Badge>;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return <span className="muted">—</span>;
    const shown = value.slice(0, 2).map(String).join(', ');
    return <span>{shown}{value.length > 2 ? ` +${value.length - 2}` : ''}</span>;
  }

  if (typeof value === 'object') return <span className="muted">…</span>;

  const text = String(value);

  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleString()}</span>;
  }

  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleDateString()}</span>;
  }

  return text.length > 70 ? <span title={text}>{text.slice(0, 70)}…</span> : text;
}

function capitalise(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function plural(value: string) {
  if (value.endsWith('y') && !/[aeiou]y$/.test(value)) return `${value.slice(0, -1)}ies`;
  if (/(s|x|z|ch|sh)$/.test(value)) return `${value}es`;
  return `${value}s`;
}
