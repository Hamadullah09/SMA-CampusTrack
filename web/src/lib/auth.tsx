import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, tokenStore, type AuthResult, type UserProfile } from '@/api/client';

interface AuthContextValue {
  user: UserProfile | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  signIn: (userNameOrEmail: string, password: string) => Promise<UserProfile>;
  signOut: () => Promise<void>;
  can: (permission: string) => boolean;
  canAny: (...permissions: string[]) => boolean;
  isRole: (role: string) => boolean;
  refreshProfile: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(() => tokenStore.user);
  const [isLoading, setIsLoading] = useState(true);

  // Re-read the profile on mount so a permission change made while the user was away takes
  // effect on their next visit rather than lingering until the cached token expires.
  useEffect(() => {
    let cancelled = false;

    async function hydrate() {
      if (!tokenStore.access) {
        setIsLoading(false);
        return;
      }

      try {
        const { data } = await api.get<UserProfile>('/auth/me');
        if (!cancelled) setUser(data);
      } catch {
        // The interceptor already handles a failed refresh; anything else means the stored
        // session is unusable, so start clean rather than showing a half-signed-in shell.
        if (!cancelled) {
          tokenStore.clear();
          setUser(null);
        }
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    }

    void hydrate();
    return () => {
      cancelled = true;
    };
  }, []);

  const signIn = useCallback(async (userNameOrEmail: string, password: string) => {
    const { data } = await api.post<AuthResult>('/auth/login', {
      userNameOrEmail,
      password,
      platform: 'Web',
      deviceName: navigator.userAgent.slice(0, 120),
    });

    tokenStore.save(data);
    setUser(data.user);
    return data.user;
  }, []);

  const signOut = useCallback(async () => {
    const refreshToken = tokenStore.refresh;

    try {
      // Best effort: revoking server-side is preferable, but a network failure must not
      // prevent the local session being cleared.
      if (refreshToken) await api.post('/auth/logout', { refreshToken });
    } catch {
      /* ignored deliberately */
    } finally {
      tokenStore.clear();
      setUser(null);
    }
  }, []);

  const refreshProfile = useCallback(async () => {
    const { data } = await api.get<UserProfile>('/auth/me');
    setUser(data);
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const permissions = new Set(user?.permissions ?? []);
    // SuperAdmin's grants are enforced server-side by role, so the client mirrors that rule
    // rather than relying on an exhaustive permission list in the token.
    const isSuperAdmin = user?.roles.includes('SuperAdmin') ?? false;

    return {
      user,
      isAuthenticated: Boolean(user),
      isLoading,
      signIn,
      signOut,
      refreshProfile,
      can: (permission: string) => isSuperAdmin || permissions.has(permission),
      canAny: (...list: string[]) => isSuperAdmin || list.some((p) => permissions.has(p)),
      isRole: (role: string) => user?.roles.includes(role) ?? false,
    };
  }, [user, isLoading, signIn, signOut, refreshProfile]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside an AuthProvider');
  return context;
}

/** The permission catalogue, mirrored from the backend so screens can gate on constants. */
export const P = {
  dashboardAdmin: 'dashboard.admin.view',
  dashboardTeacher: 'dashboard.teacher.view',
  studentsView: 'students.view',
  studentsCreate: 'students.create',
  studentsEdit: 'students.edit',
  studentsDelete: 'students.delete',
  teachersView: 'teachers.view',
  teachersCreate: 'teachers.create',
  guardiansView: 'guardians.view',
  guardiansCreate: 'guardians.create',
  guardiansManageLinks: 'guardians.links.manage',
  classesView: 'academics.classes.view',
  classesManage: 'academics.classes.manage',
  subjectsView: 'academics.subjects.view',
  subjectsManage: 'academics.subjects.manage',
  sessionsManage: 'academics.sessions.manage',
  enrollmentsManage: 'academics.enrollments.manage',
  classroomsView: 'classrooms.view',
  classroomsManage: 'classrooms.manage',
  timetableView: 'timetable.view',
  timetableManage: 'timetable.manage',
  attendanceView: 'attendance.view',
  attendanceViewAssigned: 'attendance.view.assigned',
  attendanceMark: 'attendance.mark',
  attendanceCorrect: 'attendance.correct',
  rfidEvents: 'rfid.events.view',
  rfidReaders: 'rfid.readers.view',
  rfidManageReaders: 'rfid.readers.manage',
  rfidLocations: 'rfid.locations.view',
  rfidManageLocations: 'rfid.locations.manage',
  rfidTags: 'rfid.tags.view',
  rfidManageTags: 'rfid.tags.manage',
  rfidMonitor: 'rfid.monitor',
  rfidSimulate: 'rfid.simulate',
  assignmentsView: 'assignments.view',
  assignmentsCreate: 'assignments.create',
  assignmentsGrade: 'assignments.grade',
  quizzesView: 'quizzes.view',
  quizzesCreate: 'quizzes.create',
  examsView: 'exams.view',
  gradesView: 'grades.view',
  gradesViewAssigned: 'grades.view.assigned',
  gradesManage: 'grades.manage',
  notificationsSend: 'notifications.send',
  announcementsView: 'announcements.view',
  announcementsManage: 'announcements.manage',
  eventsView: 'events.view',
  eventsManage: 'events.manage',
  leaveView: 'leave.view',
  leaveApprove: 'leave.approve',
  reportsAttendance: 'reports.attendance.view',
  reportsAcademic: 'reports.academic.view',
  reportsRfid: 'reports.rfid.view',
  usersView: 'users.view',
  rolesView: 'roles.view',
  auditView: 'audit.view',
  settingsView: 'settings.view',
  settingsManage: 'settings.manage',
} as const;
