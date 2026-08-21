import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api, describeError } from '@/api/client';
import {
  Badge, Card, EmptyState, ErrorState, Icon, Stat,
} from '@/components/ui';

interface TeacherDashboardData {
  teacherId: number;
  teacherName: string;
  sectionCount: number;
  studentCount: number;
  subjectCount: number;
  todayLessons: Lesson[];
  pendingSubmissions: number;
  pendingQuizGrading: number;
  assignmentsDueThisWeek: number;
  averageAttendance: number;
  studentsAtRisk: AtRisk[];
  unreadNotifications: number;
}

interface Lesson {
  slotId: number;
  subjectName: string;
  sectionName: string;
  classroomName?: string;
  startTime: string;
  endTime: string;
  studentCount: number;
  attendanceTaken: boolean;
  isInProgress: boolean;
  isMonitored: boolean;
}

interface AtRisk {
  studentId: number;
  studentName: string;
  sectionName?: string;
  attendancePercentage: number;
  reason: string;
}

/**
 * The teacher's landing screen: what they are teaching today, what is waiting to be marked,
 * and which students need attention. Everything is scoped server-side to their assignments.
 */
export function TeacherDashboard() {
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['dashboard', 'teacher'],
    queryFn: async () => (await api.get<TeacherDashboardData>('/dashboard/teacher')).data,
    refetchInterval: 120_000,
  });

  if (isLoading) {
    return (
      <>
        <div className="page-header"><h1 className="page-title">My teaching</h1></div>
        <div className="stat-grid">
          {Array.from({ length: 4 }, (_, i) => <Stat key={i} label="" value="" loading />)}
        </div>
      </>
    );
  }

  if (isError || !data) {
    return (
      <>
        <div className="page-header"><h1 className="page-title">My teaching</h1></div>
        <Card>
          <ErrorState message={describeError(error)} onRetry={() => void refetch()} />
        </Card>
      </>
    );
  }

  const pendingTotal = data.pendingSubmissions + data.pendingQuizGrading;

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Good day, {data.teacherName.split(' ')[0]}</h1>
          <p className="page-subtitle">
            {data.todayLessons.length === 0
              ? 'You have no lessons scheduled today.'
              : `${data.todayLessons.length} lesson${data.todayLessons.length === 1 ? '' : 's'} today`}
          </p>
        </div>
      </div>

      <div className="stat-grid">
        <Stat label="My students" value={data.studentCount} meta={`${data.sectionCount} class(es)`} icon="users" />
        <Stat
          label="Awaiting marking" value={pendingTotal}
          meta={`${data.pendingSubmissions} submissions · ${data.pendingQuizGrading} quizzes`}
          icon="file" accent={pendingTotal > 0 ? 'warning' : 'success'}
        />
        <Stat
          label="Average attendance" value={`${data.averageAttendance}%`}
          meta="Across my classes, last 30 days" icon="check"
          accent={data.averageAttendance >= 90 ? 'success' : data.averageAttendance >= 75 ? 'warning' : 'danger'}
        />
        <Stat label="Due this week" value={data.assignmentsDueThisWeek} icon="calendar" accent="info" />
      </div>

      <div className="dash-grid">
        <Card title="Today's lessons" subtitle="Take the register from here" flush className="dash-span-2">
          {data.todayLessons.length === 0 ? (
            <EmptyState
              title="Nothing scheduled today"
              message="Your timetable has no lessons for today."
              icon="calendar"
            />
          ) : (
            <ul className="event-feed">
              {data.todayLessons.map((lesson) => (
                <li key={lesson.slotId} className={lesson.isInProgress ? 'is-new' : ''}>
                  <span className={`event-dot ${lesson.isInProgress ? 'event-in' : 'event-out'}`}>
                    <Icon name="clock" size={13} />
                  </span>

                  <div className="event-body">
                    <strong>
                      {lesson.subjectName} · {lesson.sectionName}
                      {lesson.isInProgress && (
                        <Badge tone="success" live>&nbsp;Now</Badge>
                      )}
                    </strong>
                    <span>
                      {formatTime(lesson.startTime)}–{formatTime(lesson.endTime)}
                      {lesson.classroomName ? ` · ${lesson.classroomName}` : ''}
                      {` · ${lesson.studentCount} students`}
                    </span>
                  </div>

                  <div className="row">
                    {lesson.isMonitored && (
                      <Badge tone="info" title="Attendance is recorded automatically by RFID">
                        <Icon name="rfid" size={11} /> Auto
                      </Badge>
                    )}
                    {lesson.attendanceTaken ? (
                      <Badge tone="success"><Icon name="check" size={11} /> Register taken</Badge>
                    ) : (
                      <Link to={`/attendance?slot=${lesson.slotId}`} className="btn btn-primary btn-sm">
                        Take register
                      </Link>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card
          title="Students needing attention"
          subtitle="Attendance below the school requirement"
          flush
          className="dash-span-2"
        >
          {data.studentsAtRisk.length === 0 ? (
            <EmptyState
              title="Everyone is on track"
              message="No student in your classes is below the attendance requirement."
              icon="check"
            />
          ) : (
            <ul className="event-feed">
              {data.studentsAtRisk.map((student) => (
                <li key={student.studentId}>
                  <span className="event-dot event-rejected"><Icon name="alert" size={13} /></span>
                  <div className="event-body">
                    <strong>{student.studentName}</strong>
                    <span>{student.sectionName} · {student.reason}</span>
                  </div>
                  <Badge tone={student.attendancePercentage < 60 ? 'danger' : 'warning'}>
                    {student.attendancePercentage}%
                  </Badge>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </>
  );
}

/** The API sends TimeOnly as "HH:mm:ss"; show it the way a timetable reads. */
function formatTime(value: string) {
  const [hours, minutes] = value.split(':');
  const date = new Date();
  date.setHours(Number(hours), Number(minutes), 0, 0);
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}
