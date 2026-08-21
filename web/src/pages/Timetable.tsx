import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import {
  Button, Card, ConfirmDialog, EmptyState, ErrorState, Icon,
  LoadingState, Modal, useToast,
} from '@/components/ui';
import './timetable.css';

interface Period { id: number; name: string; sequence: number; startTime: string; endTime: string; isBreak: boolean; }
interface Slot {
  id: number; dayOfWeek: number; dayName: string; periodId: number; periodName: string;
  startTime: string; endTime: string; isBreak: boolean;
  sectionId: number; sectionName: string;
  subjectId: number; subjectName: string; subjectColour?: string;
  teacherId?: number; teacherName?: string;
  classroomId?: number; classroomName?: string;
  isMonitored: boolean;
}
interface Option { id: number; displayName?: string; name?: string; fullName?: string; }

const DAYS = [
  { value: 1, label: 'Monday', short: 'Mon' },
  { value: 2, label: 'Tuesday', short: 'Tue' },
  { value: 3, label: 'Wednesday', short: 'Wed' },
  { value: 4, label: 'Thursday', short: 'Thu' },
  { value: 5, label: 'Friday', short: 'Fri' },
  { value: 6, label: 'Saturday', short: 'Sat' },
  { value: 7, label: 'Sunday', short: 'Sun' },
];

/**
 * The timetable builder.
 *
 * A grid rather than a list, because a timetable is a two-dimensional object and a school
 * office reads it that way. Clicking an empty cell creates the lesson for that exact day and
 * period, which removes the two most error-prone fields from the form entirely.
 */
export function TimetablePage() {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [sectionId, setSectionId] = useState<string>('');
  const [editing, setEditing] = useState<{ slot?: Slot; day: number; periodId: number } | null>(null);
  const [deleting, setDeleting] = useState<Slot | null>(null);

  const canManage = can(P.timetableManage);

  const sections = useQuery({
    queryKey: ['sections'],
    queryFn: async () => (await api.get<Option[]>('/academics/sections')).data,
  });

  const periods = useQuery({
    queryKey: ['periods'],
    queryFn: async () => (await api.get<Period[]>('/timetable/periods')).data,
  });

  const activeSection = sectionId || String(sections.data?.[0]?.id ?? '');

  const slots = useQuery({
    queryKey: ['timetable', activeSection],
    queryFn: async () => (await api.get<Slot[]>(`/timetable/section/${activeSection}`)).data,
    enabled: Boolean(activeSection),
  });

  const remove = useMutation({
    mutationFn: (slot: Slot) => api.delete(`/timetable/slots/${slot.id}`),
    onSuccess: () => {
      toast.success('Lesson removed');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['timetable'] });
    },
    onError: (error) => toast.error('Could not remove the lesson', describeError(error)),
  });

  // Indexed by "day:period" so a cell lookup is O(1) rather than a scan per cell.
  const grid = useMemo(() => {
    const map = new Map<string, Slot>();
    for (const slot of slots.data ?? []) map.set(`${slot.dayOfWeek}:${slot.periodId}`, slot);
    return map;
  }, [slots.data]);

  const teachingDays = DAYS.slice(0, 5);
  const orderedPeriods = [...(periods.data ?? [])].sort((a, b) => a.sequence - b.sequence);

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Timetable</h1>
          <p className="page-subtitle">
            Clashes are checked as you save — a teacher or room cannot be double-booked
          </p>
        </div>

        <div className="row wrap">
          <select
            className="select" style={{ width: 'auto', minWidth: 190 }}
            value={activeSection} onChange={(e) => setSectionId(e.target.value)}
            aria-label="Choose a section"
          >
            {sections.data?.map((section) => (
              <option key={section.id} value={section.id}>{section.displayName}</option>
            ))}
          </select>

          {canManage && <PeriodsButton onCreated={() => void periods.refetch()} />}
        </div>
      </div>

      {periods.isLoading || sections.isLoading ? (
        <Card><LoadingState rows={6} /></Card>
      ) : orderedPeriods.length === 0 ? (
        <Card>
          <EmptyState
            icon="clock"
            title="No periods defined yet"
            message="Define the school's bell times first — every lesson sits in a period."
            action={canManage ? <PeriodsButton onCreated={() => void periods.refetch()} primary /> : undefined}
          />
        </Card>
      ) : !activeSection ? (
        <Card>
          <EmptyState
            icon="building"
            title="No sections yet"
            message="Create a class and a section before building a timetable."
          />
        </Card>
      ) : slots.isError ? (
        <Card><ErrorState message={describeError(slots.error)} onRetry={() => void slots.refetch()} /></Card>
      ) : (
        <Card flush>
          <div className="table-wrap">
            <table className="timetable-grid">
              <thead>
                <tr>
                  <th className="tt-period-head">Period</th>
                  {teachingDays.map((day) => (
                    <th key={day.value}>
                      <span className="tt-day-full">{day.label}</span>
                      <span className="tt-day-short">{day.short}</span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {orderedPeriods.map((period) => (
                  <tr key={period.id} className={period.isBreak ? 'tt-break-row' : ''}>
                    <th className="tt-period">
                      <strong>{period.name}</strong>
                      <span className="tabular">{shortTime(period.startTime)}–{shortTime(period.endTime)}</span>
                    </th>

                    {teachingDays.map((day) => {
                      // Breaks span the whole row: nothing is ever scheduled in them.
                      if (period.isBreak) {
                        return <td key={day.value} className="tt-break">Break</td>;
                      }

                      const slot = grid.get(`${day.value}:${period.id}`);

                      if (!slot) {
                        return (
                          <td key={day.value} className="tt-empty">
                            {canManage && (
                              <button
                                className="tt-add"
                                onClick={() => setEditing({ day: day.value, periodId: period.id })}
                                aria-label={`Add a lesson on ${day.label} in ${period.name}`}
                              >
                                <Icon name="plus" size={14} />
                              </button>
                            )}
                          </td>
                        );
                      }

                      return (
                        <td key={day.value} className="tt-filled">
                          <div
                            className="tt-lesson"
                            style={{ borderLeftColor: slot.subjectColour ?? 'var(--brand-500)' }}
                          >
                            <strong>{slot.subjectName}</strong>
                            {slot.teacherName && <span>{slot.teacherName}</span>}
                            <span className="muted">
                              {slot.classroomName ?? 'No room'}
                              {/* Tells the office at a glance which lessons will have
                                  attendance taken automatically. */}
                              {slot.isMonitored && (
                                <Icon name="rfid" size={11} />
                              )}
                            </span>

                            {canManage && (
                              <div className="tt-actions">
                                <button
                                  onClick={() => setEditing({ slot, day: day.value, periodId: period.id })}
                                  aria-label="Edit lesson"
                                >
                                  <Icon name="edit" size={12} />
                                </button>
                                <button onClick={() => setDeleting(slot)} aria-label="Remove lesson">
                                  <Icon name="trash" size={12} />
                                </button>
                              </div>
                            )}
                          </div>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {editing && (
        <SlotDialog
          sectionId={Number(activeSection)}
          day={editing.day}
          periodId={editing.periodId}
          slot={editing.slot}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['timetable'] });
          }}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        title="Remove this lesson?"
        message={
          `${deleting?.subjectName} on ${deleting?.dayName} will be removed from the timetable. ` +
          'Attendance already recorded against it is kept.'
        }
        confirmLabel="Remove lesson"
        danger
        loading={remove.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
      />
    </>
  );
}

function SlotDialog({
  sectionId, day, periodId, slot, onClose, onSaved,
}: {
  sectionId: number; day: number; periodId: number; slot?: Slot;
  onClose: () => void; onSaved: () => void;
}) {
  const toast = useToast();

  const [subjectId, setSubjectId] = useState(String(slot?.subjectId ?? ''));
  const [teacherId, setTeacherId] = useState(String(slot?.teacherId ?? ''));
  const [classroomId, setClassroomId] = useState(String(slot?.classroomId ?? ''));
  const [conflicts, setConflicts] = useState<Array<{ kind: string; message: string }>>([]);

  const subjects = useQuery({
    queryKey: ['subjects'],
    queryFn: async () => (await api.get<Option[]>('/academics/subjects')).data,
  });

  const teachers = useQuery({
    queryKey: ['teachers-all'],
    queryFn: async () =>
      (await api.get<{ items: Option[] }>('/teachers', { params: { pageSize: 200 } })).data.items,
  });

  const rooms = useQuery({
    queryKey: ['classrooms'],
    queryFn: async () => (await api.get<Option[]>('/academics/classrooms')).data,
  });

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        id: slot?.id,
        sectionId,
        subjectId: Number(subjectId),
        teacherId: teacherId ? Number(teacherId) : undefined,
        classroomId: classroomId ? Number(classroomId) : undefined,
        timetablePeriodId: periodId,
        dayOfWeek: day,
      };

      // Checked before saving so the user sees the clash next to the fields that caused it,
      // rather than as a failed save.
      const { data: found } = await api.post<Array<{ kind: string; message: string }>>(
        '/timetable/check-conflicts', body,
      );

      if (found.length > 0) {
        setConflicts(found);
        throw new Error('conflicts');
      }

      return api.post('/timetable/slots', body);
    },
    onSuccess: () => {
      toast.success(slot ? 'Lesson updated' : 'Lesson added');
      onSaved();
    },
    onError: (error) => {
      if ((error as Error).message === 'conflicts') return;
      toast.error('Could not save the lesson', describeError(error));
    },
  });

  const dayName = DAYS.find((d) => d.value === day)?.label ?? '';

  return (
    <Modal
      open
      onClose={onClose}
      title={slot ? 'Edit lesson' : `Add a lesson — ${dayName}`}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button
            variant="primary" loading={save.isPending} disabled={!subjectId}
            onClick={() => { setConflicts([]); save.mutate(); }}
          >
            {slot ? 'Save lesson' : 'Add lesson'}
          </Button>
        </>
      }
    >
      {conflicts.length > 0 && (
        <div className="alert alert-error" style={{ marginBottom: 'var(--space-4)' }}>
          <Icon name="alert" />
          <div>
            <div className="alert-title">This slot clashes</div>
            <div className="alert-body">
              {conflicts.map((conflict, index) => <div key={index}>{conflict.message}</div>)}
            </div>
          </div>
        </div>
      )}

      <div className="stack">
        <div className="field">
          <label className="label label-required" htmlFor="tt-subject">Subject</label>
          <select
            id="tt-subject" className="select" value={subjectId}
            onChange={(e) => setSubjectId(e.target.value)}
          >
            <option value="">Choose a subject</option>
            {subjects.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </div>

        <div className="field">
          <label className="label" htmlFor="tt-teacher">Teacher</label>
          <select
            id="tt-teacher" className="select" value={teacherId}
            onChange={(e) => setTeacherId(e.target.value)}
          >
            <option value="">Not assigned</option>
            {teachers.data?.map((t) => <option key={t.id} value={t.id}>{t.fullName}</option>)}
          </select>
        </div>

        <div className="field">
          <label className="label" htmlFor="tt-room">Room</label>
          <select
            id="tt-room" className="select" value={classroomId}
            onChange={(e) => setClassroomId(e.target.value)}
          >
            <option value="">No room</option>
            {rooms.data?.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
          </select>
          <span className="field-hint">
            If the room has an RFID reader, attendance for this lesson is taken automatically.
          </span>
        </div>
      </div>
    </Modal>
  );
}

/** Defining bell times is a prerequisite for any timetable, so it lives beside it. */
function PeriodsButton({ onCreated, primary }: { onCreated: () => void; primary?: boolean }) {
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ name: '', sequence: 1, startTime: '08:00', endTime: '08:45', isBreak: false });

  const create = useMutation({
    mutationFn: () => api.post('/timetable/periods', {
      ...form,
      sequence: Number(form.sequence),
      startTime: `${form.startTime}:00`,
      endTime: `${form.endTime}:00`,
    }),
    onSuccess: () => {
      toast.success('Period added');
      setOpen(false);
      setForm({ name: '', sequence: form.sequence + 1, startTime: '08:00', endTime: '08:45', isBreak: false });
      onCreated();
    },
    onError: (error) => toast.error('Could not add the period', describeError(error)),
  });

  return (
    <>
      <Button variant={primary ? 'primary' : 'secondary'} icon="clock" onClick={() => setOpen(true)}>
        Add period
      </Button>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Add a period"
        size="sm"
        footer={
          <>
            <Button onClick={() => setOpen(false)}>Cancel</Button>
            <Button
              variant="primary" loading={create.isPending}
              disabled={!form.name.trim()} onClick={() => create.mutate()}
            >
              Add period
            </Button>
          </>
        }
      >
        <div className="stack">
          <div className="field">
            <label className="label label-required" htmlFor="p-name">Name</label>
            <input
              id="p-name" className="input" value={form.name} placeholder="Period 1"
              onChange={(e) => setForm({ ...form, name: e.target.value })}
            />
          </div>

          <div className="form-grid">
            <div className="field">
              <label className="label" htmlFor="p-seq">Order</label>
              <input
                id="p-seq" className="input" type="number" min={1} value={form.sequence}
                onChange={(e) => setForm({ ...form, sequence: Number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label className="label" htmlFor="p-start">Starts</label>
              <input
                id="p-start" className="input" type="time" value={form.startTime}
                onChange={(e) => setForm({ ...form, startTime: e.target.value })}
              />
            </div>
            <div className="field">
              <label className="label" htmlFor="p-end">Ends</label>
              <input
                id="p-end" className="input" type="time" value={form.endTime}
                onChange={(e) => setForm({ ...form, endTime: e.target.value })}
              />
            </div>
          </div>

          <label className="checkbox-row">
            <input
              type="checkbox" checked={form.isBreak}
              onChange={(e) => setForm({ ...form, isBreak: e.target.checked })}
            />
            <span>This is a break — no lessons can be scheduled in it</span>
          </label>
        </div>
      </Modal>
    </>
  );
}

function shortTime(value: string) {
  return value.slice(0, 5);
}
