import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '@/layouts/AppShell';
import { useAuth } from '@/lib/auth';
import { LoginPage } from '@/pages/Login';
import { MobileAppPage } from '@/pages/MobileApp';
import { AdminDashboard } from '@/pages/AdminDashboard';
import { StudentsPage } from '@/pages/Students';
import { TeacherDashboard } from '@/pages/TeacherDashboard';
import { RfidMonitorPage } from '@/pages/RfidMonitor';
import { ReadersPage } from '@/pages/Readers';
import { AttendancePage } from '@/pages/Attendance';
import { TimetablePage } from '@/pages/Timetable';
import { GradesPage } from '@/pages/Grades';
import { ReportsPage } from '@/pages/Reports';
import { UsersPage } from '@/pages/Users';
import { SettingsPage } from '@/pages/Settings';
import { NotificationsPage } from '@/pages/Notifications';
import { GuardiansPage } from '@/pages/Guardians';
import { ResourcePage } from '@/components/resource/ResourcePage';
import { teachersResource, staffResource } from '@/features/resources/people';
import {
  classesResource, classroomsResource, coursesResource, sectionsResource,
  sessionsResource, subjectsResource, teachingAssignmentsResource,
} from '@/features/resources/academics';
import { cardsResource, eventsLogResource, locationsResource } from '@/features/resources/rfid';
import {
  announcementsResource, assignmentsResource, auditResource, eventsResource,
  examsResource, leaveResource, quizzesResource,
} from '@/features/resources/school';
import { Icon } from '@/components/ui';

/** Blocks a route until the session is known, then sends unauthenticated users to sign in. */
function RequireAuth({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  // Without this gate a refresh would flash the login screen before the stored session
  // finishes hydrating.
  if (isLoading) {
    return (
      <div style={{
        display: 'grid', placeItems: 'center', minHeight: '100dvh',
        color: 'var(--text-muted)', gap: 'var(--space-3)',
      }}>
        <span className="btn-spinner" style={{ width: 24, height: 24 }} />
        <span>Loading SMA Campus Track…</span>
      </div>
    );
  }

  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

export default function App() {
  const { isAuthenticated, user } = useAuth();
  const home = user?.primaryPortal === 'teacher' ? '/teacher' : '/admin';

  return (
    <Routes>
      <Route
        path="/login"
        element={isAuthenticated ? <Navigate to={home} replace /> : <LoginPage />}
      />

      <Route element={<RequireAuth><AppShell /></RequireAuth>}>
        <Route index element={<Navigate to={home} replace />} />

        <Route path="/admin" element={<AdminDashboard />} />
        <Route path="/teacher" element={<TeacherDashboard />} />

        {/* People */}
        <Route path="/students" element={<StudentsPage />} />
        <Route path="/teachers" element={<ResourcePage config={teachersResource} />} />
        <Route path="/guardians" element={<GuardiansPage />} />
        <Route path="/staff" element={<ResourcePage config={staffResource} />} />

        {/* Academic structure */}
        <Route path="/sessions" element={<ResourcePage config={sessionsResource} />} />
        <Route path="/classes" element={<ResourcePage config={classesResource} />} />
        <Route path="/sections" element={<ResourcePage config={sectionsResource} />} />
        <Route path="/subjects" element={<ResourcePage config={subjectsResource} />} />
        <Route path="/courses" element={<ResourcePage config={coursesResource} />} />
        <Route path="/classrooms" element={<ResourcePage config={classroomsResource} />} />
        <Route path="/teaching-assignments" element={<ResourcePage config={teachingAssignmentsResource} />} />

        {/* Teaching and learning */}
        <Route path="/timetable" element={<TimetablePage />} />
        <Route path="/attendance" element={<AttendancePage />} />
        <Route path="/assignments" element={<ResourcePage config={assignmentsResource} />} />
        <Route path="/quizzes" element={<ResourcePage config={quizzesResource} />} />
        <Route path="/exams" element={<ResourcePage config={examsResource} />} />
        <Route path="/grades" element={<GradesPage />} />

        {/* RFID */}
        <Route path="/rfid/monitor" element={<RfidMonitorPage />} />
        <Route path="/rfid/events" element={<ResourcePage config={eventsLogResource} />} />
        <Route path="/rfid/readers" element={<ReadersPage />} />
        <Route path="/rfid/locations" element={<ResourcePage config={locationsResource} />} />
        <Route path="/rfid/cards" element={<ResourcePage config={cardsResource} />} />

        {/* Communication */}
        <Route path="/announcements" element={<ResourcePage config={announcementsResource} />} />
        <Route path="/events" element={<ResourcePage config={eventsResource} />} />
        <Route path="/leave" element={<ResourcePage config={leaveResource} />} />
        <Route path="/mobile-app" element={<MobileAppPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />

        {/* Administration */}
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="/audit" element={<ResourcePage config={auditResource} />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>

      <Route path="*" element={<NotFound />} />
    </Routes>
  );
}

function NotFound() {
  return (
    <div style={{
      display: 'grid', placeItems: 'center', minHeight: '100dvh',
      textAlign: 'center', padding: 'var(--space-6)',
    }}>
      <div className="state-panel">
        <span className="state-icon"><Icon name="search" size={22} /></span>
        <h1 className="state-title">Page not found</h1>
        <p className="state-message">That page does not exist or has moved.</p>
        <a href="/" className="btn btn-primary">Back to the dashboard</a>
      </div>
    </div>
  );
}
