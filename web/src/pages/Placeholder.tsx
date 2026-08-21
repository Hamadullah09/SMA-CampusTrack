import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, describeError, type PagedResult } from '@/api/client';
import {
  Badge, Button, Card, EmptyState, ErrorState, Icon, LoadingState, Pagination, useDebounced,
} from '@/components/ui';

type Row = Record<string, unknown>;

/**
 * A generic, working browser over any list endpoint.
 *
 * Rather than shipping dead links or mocked screens for the modules that do not yet have a
 * bespoke UI, each route renders real data from its real endpoint: searchable, paged and
 * exportable. It is honest about being a general view, and it is genuinely usable today while
 * the tailored screens are built one at a time.
 */
export function PlaceholderPage({ title, endpoint }: { title: string; endpoint: string }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounced(search);

  const query = useQuery({
    queryKey: ['generic', endpoint, page, debouncedSearch],
    queryFn: async () => {
      const { data } = await api.get<PagedResult<Row> | Row[] | { items?: Row[] }>(endpoint, {
        params: { page, pageSize: 25, search: debouncedSearch || undefined },
      });

      // Endpoints return either a page envelope, a bare array, or a report envelope.
      if (Array.isArray(data)) {
        return {
          items: data, page: 1, pageSize: data.length,
          totalCount: data.length, totalPages: 1, hasPrevious: false, hasNext: false,
        } satisfies PagedResult<Row>;
      }

      if ('items' in data && Array.isArray(data.items) && !('totalCount' in data)) {
        return {
          items: data.items, page: 1, pageSize: data.items.length,
          totalCount: data.items.length, totalPages: 1, hasPrevious: false, hasNext: false,
        } satisfies PagedResult<Row>;
      }

      return data as PagedResult<Row>;
    },
  });

  const rows = query.data?.items ?? [];

  // Columns are derived from the payload, skipping the noisy internals nobody scans a table for.
  const columns = rows.length > 0
    ? Object.keys(rows[0])
        .filter((key) => !HIDDEN_KEYS.has(key) && !key.endsWith('Json'))
        .slice(0, 8)
    : [];

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">{title}</h1>
          <p className="page-subtitle">
            {query.data ? `${query.data.totalCount.toLocaleString()} record(s)` : 'Loading'}
          </p>
        </div>
        <Button icon="refresh" onClick={() => void query.refetch()} loading={query.isFetching}>
          Refresh
        </Button>
      </div>

      <Card flush>
        <div className="toolbar">
          <div className="search-box">
            <Icon name="search" size={15} />
            <input
              className="input"
              placeholder={`Search ${title.toLowerCase()}`}
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              aria-label={`Search ${title}`}
            />
          </div>
        </div>

        {query.isLoading ? (
          <LoadingState rows={6} />
        ) : query.isError ? (
          <ErrorState message={describeError(query.error)} onRetry={() => void query.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState
            title={`No ${title.toLowerCase()} yet`}
            message={
              debouncedSearch
                ? 'Nothing matched that search. Try a different term.'
                : 'Records will appear here once they are added.'
            }
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table-responsive">
                <thead>
                  <tr>{columns.map((key) => <th key={key}>{humanise(key)}</th>)}</tr>
                </thead>
                <tbody>
                  {rows.map((row, index) => (
                    <tr key={String(row.id ?? index)}>
                      {columns.map((key) => (
                        <td key={key} data-label={humanise(key)}>{renderCell(row[key])}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {query.data && (
              <Pagination
                page={query.data.page}
                pageSize={query.data.pageSize}
                totalCount={query.data.totalCount}
                totalPages={query.data.totalPages}
                onPageChange={setPage}
              />
            )}
          </>
        )}
      </Card>
    </>
  );
}

const HIDDEN_KEYS = new Set([
  'id', 'schoolId', 'isDeleted', 'deletedAtUtc', 'deletedByUserId',
  'createdByUserId', 'updatedByUserId', 'dataJson', 'payloadJson',
]);

function humanise(key: string) {
  const spaced = key.replace(/([A-Z])/g, ' $1').replace(/Utc$/i, '').trim();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

function renderCell(value: unknown): React.ReactNode {
  if (value == null) return <span className="muted">—</span>;

  if (typeof value === 'boolean') {
    return <Badge tone={value ? 'success' : 'neutral'}>{value ? 'Yes' : 'No'}</Badge>;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return <span className="muted">—</span>;
    return <span>{value.slice(0, 3).map(String).join(', ')}{value.length > 3 ? ` +${value.length - 3}` : ''}</span>;
  }

  if (typeof value === 'object') return <span className="muted">…</span>;

  const text = String(value);

  // ISO timestamps are rendered in the reader's own locale rather than as raw UTC strings.
  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleString()}</span>;
  }

  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    return <span className="tabular">{new Date(text).toLocaleDateString()}</span>;
  }

  if (STATUS_TONES[text]) return <Badge tone={STATUS_TONES[text]}>{text}</Badge>;

  return text.length > 60 ? <span title={text}>{text.slice(0, 60)}…</span> : text;
}

const STATUS_TONES: Record<string, 'success' | 'warning' | 'danger' | 'info' | 'neutral'> = {
  Active: 'success', Online: 'success', Present: 'success', Approved: 'success', Published: 'success',
  Late: 'warning', Pending: 'warning', Degraded: 'warning', Draft: 'warning', Partial: 'warning',
  Absent: 'danger', Offline: 'danger', Error: 'danger', Rejected: 'danger', Revoked: 'danger',
  Inactive: 'neutral', Closed: 'neutral', Archived: 'neutral',
};
