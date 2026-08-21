import { useState } from 'react';

import { api, describeError } from '@/api/client';
import { Button, Modal, useToast } from '@/components/ui';
import { ResourceField } from '@/components/resource/ResourceForm';
import type { FieldConfig } from '@/components/resource/types';

/**
 * Card assignment and revocation.
 *
 * These are not ordinary CRUD writes, which is why they are actions rather than an edit
 * form. A card's EPC is burned into the tag and is never edited; what changes is which
 * student it belongs to. Assignment and revocation are also the two points where the
 * mapping between a physical tag and a pupil's identity moves, so each is deliberate and
 * confirmed rather than a field someone can alter in passing.
 */

/** The student picker is fetched from the API, so it always reflects real enrolment. */
const STUDENT_FIELD: FieldConfig = {
  name: 'studentId',
  label: 'Assign to student',
  type: 'select',
  required: true,
  optionsFrom: {
    endpoint: '/students',
    valueKey: 'id',
    labelKey: 'fullName',
    params: { pageSize: 300 },
  },
};

const EPC_FIELD: FieldConfig = {
  name: 'epc',
  label: 'Card EPC',
  type: 'text',
  required: true,
  placeholder: 'E2004707A1B2C3D4E5F60011',
  hint: 'Read the tag with a reader or scan its printed label.',
};

/** The states a card can be withdrawn into, mirroring RfidTagStatus on the API. */
const REVOKE_REASONS = [
  { value: 'Revoked', label: 'Withdrawn by the school' },
  { value: 'Lost', label: 'Reported lost' },
  { value: 'Damaged', label: 'Damaged' },
];

const CARD_NUMBER_FIELD: FieldConfig = {
  name: 'cardNumber',
  label: 'Printed card number',
  type: 'text',
  placeholder: 'Optional',
};

export function AssignCardButton({ onDone }: { onDone: () => void }) {
  const [open, setOpen] = useState(false);
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const toast = useToast();

  const set = (name: string) => (value: unknown) =>
    setValues((current) => ({ ...current, [name]: value }));

  async function submit() {
    if (!values.epc || !values.studentId) {
      setError('Choose a student and enter the card EPC.');
      return;
    }

    setSaving(true);
    setError(null);

    try {
      const { data } = await api.post<{ replacedCount: number }>('/rfid/tags/assign', {
        epc: String(values.epc).trim().toUpperCase(),
        studentId: Number(values.studentId),
        cardNumber: values.cardNumber ? String(values.cardNumber).trim() : undefined,
      });

      // A student may only hold one active card, so the API retires any previous one.
      // Saying so avoids the surprise of an old card silently ceasing to work.
      toast.success(
        'Card assigned',
        data.replacedCount > 0
          ? `${data.replacedCount} previous card was retired.`
          : undefined,
      );

      setValues({});
      setOpen(false);
      onDone();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Button icon="plus" onClick={() => setOpen(true)}>Assign card</Button>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Assign a card"
        footer={
          <>
            <Button onClick={() => setOpen(false)} disabled={saving}>Cancel</Button>
            <Button variant="primary" onClick={() => void submit()} loading={saving}>
              Assign card
            </Button>
          </>
        }
      >
        <div className="form-grid">
          {[EPC_FIELD, STUDENT_FIELD, CARD_NUMBER_FIELD].map((field) => (
            <ResourceField
              key={field.name}
              field={field}
              value={values[field.name]}
              values={values}
              disabled={saving}
              onChange={set(field.name)}
            />
          ))}
        </div>

        {error && <p className="field-error" role="alert">{error}</p>}
      </Modal>
    </>
  );
}

export function RevokeCardButton({
  tagId,
  epc,
  onDone,
}: {
  tagId: number;
  epc: string;
  onDone: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [working, setWorking] = useState(false);
  const [status, setStatus] = useState('Revoked');
  const [reason, setReason] = useState('');
  const toast = useToast();

  async function revoke() {
    setWorking(true);

    try {
      // Why a card stopped being valid matters later: a lost card may turn up and be
      // re-issued, a damaged one is replaced, and a revoked one was withdrawn on purpose.
      await api.post(`/rfid/tags/${tagId}/revoke`, {
        status,
        reason: reason.trim() || undefined,
      });

      toast.success('Card withdrawn', 'It will no longer be recognised at any reader.');
      setOpen(false);
      setReason('');
      onDone();
    } catch (caught) {
      toast.error('Could not withdraw the card', describeError(caught));
    } finally {
      setWorking(false);
    }
  }

  return (
    <>
      <Button
        size="sm"
        variant="ghost"
        icon="shield"
        aria-label="Withdraw card"
        onClick={() => setOpen(true)}
      />

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Withdraw this card"
        size="sm"
        footer={
          <>
            <Button onClick={() => setOpen(false)} disabled={working}>Cancel</Button>
            <Button variant="danger" onClick={() => void revoke()} loading={working}>
              Withdraw card
            </Button>
          </>
        }
      >
        <p style={{ color: 'var(--text-secondary)', marginBottom: 'var(--space-4)' }}>
          Card <span className="mono">{epc}</span> will stop being recognised at every reader
          immediately. Assign a replacement to restore access.
        </p>

        <div className="form-grid">
          <ResourceField
            field={{
              name: 'status',
              label: 'Reason',
              type: 'select',
              required: true,
              options: REVOKE_REASONS,
            }}
            value={status}
            values={{ status }}
            disabled={working}
            onChange={(value) => setStatus(String(value))}
          />

          <ResourceField
            field={{
              name: 'reason',
              label: 'Note',
              type: 'textarea',
              placeholder: 'Optional detail for the audit trail',
            }}
            value={reason}
            values={{ reason }}
            disabled={working}
            onChange={(value) => setReason(String(value ?? ''))}
          />
        </div>
      </Modal>
    </>
  );
}
