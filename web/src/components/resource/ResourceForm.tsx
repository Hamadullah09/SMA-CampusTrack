import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import { Icon } from '@/components/ui';
import type { FieldConfig } from './types';

/**
 * Renders one form field from its declaration and keeps its own async options.
 *
 * Options are fetched per field rather than hoisted into the parent so a form can mix
 * static and API-backed choices without the parent knowing which is which — and so a
 * dependent field (sections filtered by class) refetches on its own when its parent
 * value changes.
 */
export function ResourceField({
  field,
  value,
  values,
  error,
  disabled,
  onChange,
}: {
  field: FieldConfig;
  value: unknown;
  values: Record<string, unknown>;
  error?: string;
  disabled?: boolean;
  onChange: (value: unknown) => void;
}) {
  const remote = field.optionsFrom;

  // Dependent params are resolved from the current form values, so "sections in the
  // selected class" reloads when the class changes.
  const resolvedParams = useMemo(() => {
    if (!remote?.params) return undefined;

    const out: Record<string, unknown> = {};
    for (const [key, raw] of Object.entries(remote.params)) {
      out[key] = typeof raw === 'string' && raw.startsWith('$')
        ? values[raw.slice(1)]
        : raw;
    }
    return out;
  }, [remote, values]);

  const optionsQuery = useQuery({
    queryKey: ['field-options', remote?.endpoint, resolvedParams],
    queryFn: async () => {
      const { data } = await api.get(remote!.endpoint, { params: resolvedParams });
      const rows = Array.isArray(data) ? data : (data.items ?? []);

      return (rows as Array<Record<string, unknown>>).map((row) => {
        const labelKey = remote!.labelKey ?? 'name';
        const label = typeof labelKey === 'function' ? labelKey(row) : String(row[labelKey] ?? '');
        return { value: row[remote!.valueKey ?? 'id'] as string | number, label };
      });
    },
    enabled: Boolean(remote),
    staleTime: 60_000,
  });

  const options = field.options ?? optionsQuery.data ?? [];
  const inputId = `field-${field.name}`;

  const common = {
    id: inputId,
    disabled,
    'aria-invalid': Boolean(error),
    'aria-describedby': error ? `${inputId}-error` : field.hint ? `${inputId}-hint` : undefined,
  };

  return (
    <div className="field" style={field.fullWidth ? { gridColumn: '1 / -1' } : undefined}>
      <label className={`label ${field.required ? 'label-required' : ''}`} htmlFor={inputId}>
        {field.label}
      </label>

      {field.type === 'textarea' ? (
        <textarea
          {...common}
          className={`textarea ${error ? 'textarea-error' : ''}`}
          value={String(value ?? '')}
          rows={field.rows ?? 4}
          placeholder={field.placeholder}
          onChange={(e) => onChange(e.target.value)}
        />
      ) : field.type === 'select' ? (
        <select
          {...common}
          className={`select ${error ? 'select-error' : ''}`}
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
        >
          <option value="">
            {optionsQuery.isLoading ? 'Loading…' : field.placeholder ?? 'Choose…'}
          </option>
          {options.map((option) => (
            <option key={String(option.value)} value={String(option.value)}>
              {option.label}
            </option>
          ))}
        </select>
      ) : field.type === 'multiselect' ? (
        <MultiSelect
          options={options}
          selected={Array.isArray(value) ? (value as Array<string | number>) : []}
          disabled={disabled}
          loading={optionsQuery.isLoading}
          onChange={onChange}
        />
      ) : field.type === 'checkbox' ? (
        <label className="checkbox-row" htmlFor={inputId}>
          <input
            {...common}
            type="checkbox"
            checked={Boolean(value)}
            onChange={(e) => onChange(e.target.checked)}
          />
          <span>{field.placeholder ?? field.label}</span>
        </label>
      ) : field.type === 'colour' || field.type === 'hex' ? (
        <div className="row">
          <input
            type="color"
            value={String(value ?? '#4f46e5')}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
            style={{ width: 46, height: 38, padding: 2, borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)', background: 'var(--bg-surface)' }}
            aria-label={`${field.label} colour`}
          />
          <input
            {...common}
            className={`input mono ${error ? 'input-error' : ''}`}
            value={String(value ?? '')}
            placeholder="#4f46e5"
            onChange={(e) => onChange(e.target.value)}
          />
        </div>
      ) : (
        <input
          {...common}
          className={`input ${error ? 'input-error' : ''}`}
          type={inputTypeFor(field.type)}
          value={formatValue(field, value)}
          placeholder={field.placeholder}
          min={field.min}
          max={field.max}
          step={field.step}
          onChange={(e) =>
            onChange(field.type === 'number'
              ? (e.target.value === '' ? '' : Number(e.target.value))
              : e.target.value)
          }
        />
      )}

      {error ? (
        <span className="field-error" id={`${inputId}-error`}>
          <Icon name="alert" size={12} /> {error}
        </span>
      ) : field.hint ? (
        <span className="field-hint" id={`${inputId}-hint`}>{field.hint}</span>
      ) : null}
    </div>
  );
}

function inputTypeFor(type: FieldConfig['type']) {
  switch (type) {
    case 'number': return 'number';
    case 'email': return 'email';
    case 'tel': return 'tel';
    case 'password': return 'password';
    case 'date': return 'date';
    case 'time': return 'time';
    case 'datetime': return 'datetime-local';
    default: return 'text';
  }
}

/** The API returns full ISO timestamps; the datetime-local input needs them trimmed. */
function formatValue(field: FieldConfig, value: unknown) {
  if (value == null) return '';

  if (field.type === 'datetime' && typeof value === 'string' && value.includes('T')) {
    return value.slice(0, 16);
  }

  if (field.type === 'time' && typeof value === 'string' && value.length > 5) {
    return value.slice(0, 5);
  }

  return String(value);
}

/** A checkbox list rather than a native multi-select, which is unusable on touch. */
function MultiSelect({
  options, selected, disabled, loading, onChange,
}: {
  options: Array<{ value: string | number; label: string }>;
  selected: Array<string | number>;
  disabled?: boolean;
  loading?: boolean;
  onChange: (value: Array<string | number>) => void;
}) {
  if (loading) return <div className="skeleton" style={{ height: 90 }} />;

  if (options.length === 0) {
    return <p className="field-hint">Nothing available to choose yet.</p>;
  }

  return (
    <div
      style={{
        maxHeight: 190, overflowY: 'auto', border: '1px solid var(--border-default)',
        borderRadius: 'var(--radius-md)', padding: 'var(--space-2)',
        display: 'flex', flexDirection: 'column', gap: 'var(--space-1)',
      }}
    >
      {options.map((option) => {
        const checked = selected.some((s) => String(s) === String(option.value));

        return (
          <label className="checkbox-row" key={String(option.value)}>
            <input
              type="checkbox"
              checked={checked}
              disabled={disabled}
              onChange={(e) =>
                onChange(e.target.checked
                  ? [...selected, option.value]
                  : selected.filter((s) => String(s) !== String(option.value)))
              }
            />
            <span>{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}

/**
 * Manages form state, validation and dirty tracking for a set of declared fields.
 *
 * Validation runs on submit rather than on every keystroke: flagging a required field as
 * invalid before the user has finished typing in it is hostile.
 */
export function useResourceForm(fields: FieldConfig[], initial?: Record<string, unknown>) {
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    const seeded: Record<string, unknown> = {};
    for (const field of fields) {
      seeded[field.name] = initial?.[field.name] ?? field.defaultValue ?? defaultFor(field);
    }
    setValues(seeded);
    setErrors({});
  }, [fields, initial]);

  function setValue(name: string, value: unknown) {
    setValues((current) => ({ ...current, [name]: value }));
    // Clear the error as soon as the user edits the field they were told about.
    setErrors((current) => {
      if (!current[name]) return current;
      const next = { ...current };
      delete next[name];
      return next;
    });
  }

  function validate(mode: 'create' | 'edit') {
    const found: Record<string, string> = {};

    for (const field of fields) {
      if (field.createOnly && mode === 'edit') continue;
      if (field.showWhen && !field.showWhen(values)) continue;

      const value = values[field.name];
      const empty = value === '' || value == null ||
        (Array.isArray(value) && value.length === 0);

      if (field.required && empty) {
        found[field.name] = `${field.label} is required.`;
        continue;
      }

      if (!empty && field.validate) {
        const message = field.validate(value, values);
        if (message) found[field.name] = message;
      }
    }

    setErrors(found);
    return Object.keys(found).length === 0;
  }

  /** Strips empty optional values so the API receives nulls rather than empty strings. */
  function payload(mode: 'create' | 'edit') {
    const out: Record<string, unknown> = {};

    for (const field of fields) {
      if (field.createOnly && mode === 'edit') continue;
      if (field.showWhen && !field.showWhen(values)) continue;

      const value = values[field.name];
      if (value === '' || value == null) {
        if (field.required) out[field.name] = value;
        continue;
      }

      out[field.name] = value;
    }

    return out;
  }

  return { values, errors, setValue, validate, payload, setErrors };
}

function defaultFor(field: FieldConfig): unknown {
  switch (field.type) {
    case 'checkbox': return false;
    case 'multiselect': return [];
    case 'number': return '';
    default: return '';
  }
}
