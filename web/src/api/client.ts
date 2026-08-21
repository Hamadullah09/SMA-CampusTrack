import axios, { AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios';

export const API_BASE = import.meta.env.VITE_API_BASE ?? '/api/v1';

const TOKEN_KEY = 'campustrack.access';
const REFRESH_KEY = 'campustrack.refresh';
const USER_KEY = 'campustrack.user';

export interface UserProfile {
  id: number;
  userName: string;
  fullName: string;
  email?: string;
  phoneNumber?: string;
  profileImageUrl?: string;
  roles: string[];
  permissions: string[];
  studentId?: number;
  teacherId?: number;
  guardianId?: number;
  staffMemberId?: number;
  primaryPortal: 'admin' | 'teacher' | 'student' | 'parent' | 'staff';
  mustChangePassword: boolean;
  schoolId: number;
  schoolName?: string;
}

export interface AuthResult {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
  user: UserProfile;
}

export const tokenStore = {
  get access() {
    return localStorage.getItem(TOKEN_KEY);
  },
  get refresh() {
    return localStorage.getItem(REFRESH_KEY);
  },
  get user(): UserProfile | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as UserProfile;
    } catch {
      return null;
    }
  },
  save(result: AuthResult) {
    localStorage.setItem(TOKEN_KEY, result.accessToken);
    localStorage.setItem(REFRESH_KEY, result.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(result.user));
  },
  clear() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  },
};

export const api = axios.create({
  baseURL: API_BASE,
  headers: { 'Content-Type': 'application/json' },
  timeout: 30_000,
});

api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = tokenStore.access;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

/*
 * Access tokens are short-lived by design, so a 401 mid-session is expected rather than
 * exceptional. This interceptor refreshes once and replays the original request.
 *
 * The single in-flight promise matters: a dashboard fires a dozen parallel requests, and
 * without it each 401 would start its own refresh. Because refresh tokens rotate on use, that
 * race would invalidate the family and log the user out — the exact failure the rotation
 * scheme is meant to detect.
 */
let refreshInFlight: Promise<string> | null = null;

async function refreshAccessToken(): Promise<string> {
  const refreshToken = tokenStore.refresh;
  if (!refreshToken) throw new Error('No refresh token');

  const { data } = await axios.post<AuthResult>(`${API_BASE}/auth/refresh`, { refreshToken });
  tokenStore.save(data);
  return data.accessToken;
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined;

    const isAuthCall = original?.url?.includes('/auth/');
    if (error.response?.status !== 401 || !original || original._retried || isAuthCall) {
      return Promise.reject(error);
    }

    original._retried = true;

    try {
      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null;
      });

      const token = await refreshInFlight;
      original.headers = { ...original.headers, Authorization: `Bearer ${token}` };
      return api(original);
    } catch {
      tokenStore.clear();
      // Full reload rather than a router navigation: it guarantees no stale cached data
      // from the previous session survives into the login screen.
      window.location.href = '/login';
      return Promise.reject(error);
    }
  },
);

/** Shape of the ProblemDetails responses the API returns. */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/**
 * Turns any failure into a sentence a school administrator can act on. Axios errors, network
 * failures and ProblemDetails bodies all arrive here, and none of them should reach a user
 * as a raw status code.
 */
export function describeError(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const problem = error.response?.data as ApiProblem | undefined;

    if (problem?.errors) {
      const first = Object.values(problem.errors).flat()[0];
      if (first) return first;
    }

    if (problem?.detail) return problem.detail;
    if (problem?.title) return problem.title;

    if (error.code === 'ECONNABORTED') return 'That request took too long. Please try again.';
    if (!error.response) return 'Cannot reach the server. Check your connection and try again.';

    switch (error.response.status) {
      case 401:
        return 'Your session has expired. Please sign in again.';
      case 403:
        return 'You do not have permission to do that.';
      case 404:
        return 'That item could not be found.';
      case 429:
        return 'Too many requests. Please wait a moment and try again.';
      default:
        return 'Something went wrong. Please try again.';
    }
  }

  return error instanceof Error ? error.message : 'Something went wrong.';
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}
