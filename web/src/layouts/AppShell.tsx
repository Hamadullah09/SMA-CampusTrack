import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import { P, useAuth } from '@/lib/auth';
import { Avatar, Badge, Button, Icon, type IconName } from '@/components/ui';
import { BrandMark, PRODUCT_NAME } from '@/components/Brand';
import { useRealtime } from '@/lib/realtime';
import './shell.css';

interface NavItem {
  to: string;
  label: string;
  icon: IconName;
  permission?: string;
  anyOf?: string[];
}

interface NavSection {
  heading: string;
  items: NavItem[];
}

/**
 * Navigation is declared once and filtered by permission, so a user is never shown a screen
 * the API would then refuse them. Grouping keeps a twenty-item admin menu scannable.
 */
const NAV: NavSection[] = [
  {
    heading: 'Overview',
    items: [
      { to: '/admin', label: 'Dashboard', icon: 'dashboard', permission: P.dashboardAdmin },
      { to: '/teacher', label: 'My teaching', icon: 'teacher', permission: P.dashboardTeacher },
      { to: '/rfid/monitor', label: 'Live monitor', icon: 'activity', permission: P.rfidMonitor },
    ],
  },
  {
    heading: 'People',
    items: [
      { to: '/students', label: 'Students', icon: 'users', permission: P.studentsView },
      { to: '/teachers', label: 'Teachers', icon: 'teacher', permission: P.teachersView },
      { to: '/guardians', label: 'Parents', icon: 'user', permission: P.guardiansView },
      { to: '/staff', label: 'Staff', icon: 'users', permission: 'staff.view' },
    ],
  },
  {
    heading: 'School structure',
    items: [
      { to: '/sessions', label: 'Academic sessions', icon: 'calendar', permission: 'academics.sessions.view' },
      { to: '/classes', label: 'Classes', icon: 'building', permission: P.classesView },
      { to: '/sections', label: 'Sections', icon: 'building', permission: 'academics.sections.view' },
      { to: '/subjects', label: 'Subjects', icon: 'book', permission: P.subjectsView },
      { to: '/courses', label: 'Courses', icon: 'book', permission: 'academics.courses.view' },
      { to: '/classrooms', label: 'Rooms', icon: 'door', permission: P.classroomsView },
      { to: '/teaching-assignments', label: 'Teaching assignments', icon: 'teacher', permission: 'academics.sections.view' },
    ],
  },
  {
    heading: 'Teaching',
    items: [
      { to: '/timetable', label: 'Timetable', icon: 'calendar', permission: P.timetableView },
      { to: '/attendance', label: 'Attendance', icon: 'check', anyOf: [P.attendanceView, P.attendanceViewAssigned] },
      { to: '/assignments', label: 'Assignments', icon: 'file', permission: P.assignmentsView },
      { to: '/quizzes', label: 'Quizzes', icon: 'award', permission: P.quizzesView },
      { to: '/exams', label: 'Examinations', icon: 'award', permission: P.examsView },
      { to: '/grades', label: 'Grades', icon: 'chart', anyOf: [P.gradesView, P.gradesViewAssigned] },
    ],
  },
  {
    heading: 'RFID',
    items: [
      { to: '/rfid/events', label: 'Movement log', icon: 'activity', permission: P.rfidEvents },
      { to: '/rfid/readers', label: 'Readers', icon: 'rfid', permission: P.rfidReaders },
      { to: '/rfid/locations', label: 'Locations', icon: 'door', permission: P.rfidLocations },
      { to: '/rfid/cards', label: 'Cards', icon: 'shield', permission: P.rfidTags },
    ],
  },
  {
    heading: 'Communication',
    items: [
      { to: '/announcements', label: 'Announcements', icon: 'megaphone', permission: P.announcementsView },
      { to: '/events', label: 'School events', icon: 'calendar', permission: P.eventsView },
      { to: '/leave', label: 'Leave requests', icon: 'inbox', permission: P.leaveView },
      { to: '/mobile-app', label: 'Mobile app', icon: 'download' },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { to: '/reports', label: 'Reports', icon: 'chart', anyOf: [P.reportsAttendance, P.reportsAcademic, P.reportsRfid] },
      { to: '/users', label: 'Users & roles', icon: 'shield', permission: P.usersView },
      { to: '/audit', label: 'Audit log', icon: 'file', permission: P.auditView },
      { to: '/settings', label: 'Settings', icon: 'settings', permission: P.settingsView },
    ],
  },
];

export function AppShell() {
  const { user, signOut, can, canAny } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [theme, setTheme] = useState<'light' | 'dark'>(
    () => (localStorage.getItem('campustrack.theme') as 'light' | 'dark') ?? 'light',
  );

  const { connected } = useRealtime();

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('campustrack.theme', theme);
  }, [theme]);

  // A route change on mobile should close the drawer; leaving it open hides the page the
  // user just navigated to.
  useEffect(() => setMobileNavOpen(false), [location.pathname]);

  const { data: unread } = useQuery({
    queryKey: ['notifications', 'unread'],
    queryFn: async () => (await api.get<{ count: number }>('/notifications/unread-count')).data,
    refetchInterval: 60_000,
  });

  const sections = NAV
    .map((section) => ({
      ...section,
      items: section.items.filter((item) =>
        item.anyOf ? canAny(...item.anyOf) : item.permission ? can(item.permission) : true,
      ),
    }))
    .filter((section) => section.items.length > 0);

  async function handleSignOut() {
    await signOut();
    navigate('/login', { replace: true });
  }

  return (
    <div className="shell">
      {mobileNavOpen && <div className="shell-scrim" onClick={() => setMobileNavOpen(false)} />}

      <aside className={`shell-sidebar ${mobileNavOpen ? 'is-open' : ''}`}>
        <div className="shell-brand">
          <span className="shell-logo"><BrandMark size={30} /></span>
          <div className="shell-brand-text">
            <strong>{PRODUCT_NAME}</strong>
            <span>{user?.schoolName ?? 'School management'}</span>
          </div>
        </div>

        <nav className="shell-nav" aria-label="Main">
          {sections.map((section) => (
            <div key={section.heading} className="shell-nav-group">
              <p className="shell-nav-heading">{section.heading}</p>
              {section.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => `shell-nav-item ${isActive ? 'is-active' : ''}`}
                  end={item.to === '/admin'}
                >
                  <Icon name={item.icon} />
                  <span>{item.label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <div className="shell-sidebar-footer">
          <Badge tone={connected ? 'success' : 'warning'} live={connected}>
            {connected ? 'Live' : 'Reconnecting'}
          </Badge>
        </div>
      </aside>

      <div className="shell-main">
        <header className="shell-topbar">
          <Button
            className="shell-menu-btn"
            variant="ghost" icon="menu" aria-label="Open navigation"
            onClick={() => setMobileNavOpen((open) => !open)}
          />

          <div className="grow" />

          <Button
            variant="ghost" size="sm"
            icon={theme === 'dark' ? 'sun' : 'moon'}
            aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
            onClick={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
          />

          <NavLink to="/notifications" className="shell-notif" aria-label="Notifications">
            <Icon name="bell" size={18} />
            {(unread?.count ?? 0) > 0 && (
              <span className="shell-notif-count">{unread!.count > 99 ? '99+' : unread!.count}</span>
            )}
          </NavLink>

          <div className="shell-user">
            <Avatar name={user?.fullName} url={user?.profileImageUrl} size="sm" />
            <div className="shell-user-text">
              <strong>{user?.fullName}</strong>
              <span>{user?.roles.join(', ')}</span>
            </div>
          </div>

          <Button variant="ghost" size="sm" icon="logout" aria-label="Sign out" onClick={handleSignOut} />
        </header>

        <main className="shell-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
