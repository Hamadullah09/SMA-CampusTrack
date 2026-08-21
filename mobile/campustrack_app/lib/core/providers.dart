import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/models.dart';
import '../data/repository.dart';
import 'api_client.dart';

/// Dependency wiring.
///
/// Everything the app needs is reachable from a provider, which is what lets a test swap the
/// repository for a fake without touching a single widget.

/// These three providers reference one another: the client needs auth to sign the user
/// out, auth needs the repository, and the repository needs the client. That is a legitimate
/// runtime graph -- Riverpod resolves it lazily -- but Dart cannot *infer* the variables'
/// types through the cycle, so each carries an explicit type annotation. Without them the
/// analyser reports top_level_cycle and every `ref.watch` of them degrades to `dynamic`,
/// which is what made the switch in main.dart non-exhaustive.
final Provider<TokenStorage> tokenStorageProvider =
    Provider<TokenStorage>((ref) => TokenStorage());

final Provider<ApiClient> apiClientProvider = Provider<ApiClient>((ref) {
  final storage = ref.watch(tokenStorageProvider);

  return ApiClient(
    storage: storage,
    // When refresh fails there is no recoverable session; drop back to signed-out state so
    // the router shows the sign-in screen rather than a wall of failed requests.
    onSessionExpired: () => ref.read(authProvider.notifier).forceSignOut(),
  );
});

final Provider<CampusRepository> repositoryProvider =
    Provider<CampusRepository>((ref) => CampusRepository(
      api: ref.watch(apiClientProvider),
      storage: ref.watch(tokenStorageProvider),
    ));

// ------------------------------------------------------------------- auth ----

sealed class AuthState {
  const AuthState();
}

class AuthLoading extends AuthState {
  const AuthLoading();
}

class AuthSignedOut extends AuthState {
  const AuthSignedOut({this.message});
  final String? message;
}

class AuthSignedIn extends AuthState {
  const AuthSignedIn(this.profile);
  final UserProfile profile;
}

class AuthNotifier extends StateNotifier<AuthState> {
  AuthNotifier(this._repository) : super(const AuthLoading()) {
    _restore();
  }

  final CampusRepository _repository;

  /// Restores a session from secure storage on launch.
  ///
  /// The cached profile is shown first so the app opens straight into the dashboard, then
  /// the profile is refreshed in the background — a parent opening the app to check on their
  /// child should not wait on a network round trip to see the shell.
  Future<void> _restore() async {
    final cached = await _repository.cachedProfile();

    if (cached == null) {
      state = const AuthSignedOut();
      return;
    }

    state = AuthSignedIn(cached);

    try {
      final fresh = await _repository.refreshProfile();
      if (mounted) state = AuthSignedIn(fresh);
    } on ApiException catch (error) {
      // Only a rejected session signs the user out. Being offline must not.
      if (error.isUnauthorised) {
        await _repository.signOut();
        if (mounted) state = const AuthSignedOut();
      }
    }
  }

  Future<void> signIn(String userName, String password) async {
    state = const AuthLoading();

    try {
      final profile = await _repository.signIn(userName, password);
      state = AuthSignedIn(profile);
    } catch (error) {
      state = AuthSignedOut(message: error.toString());
      rethrow;
    }
  }

  Future<void> signOut() async {
    await _repository.signOut();
    state = const AuthSignedOut();
  }

  /// Called by the API client when a refresh has already failed.
  void forceSignOut() {
    if (state is AuthSignedIn) {
      state = const AuthSignedOut(message: 'Your session has ended. Please sign in again.');
    }
  }
}

final StateNotifierProvider<AuthNotifier, AuthState> authProvider =
    StateNotifierProvider<AuthNotifier, AuthState>(
  (ref) => AuthNotifier(ref.watch(repositoryProvider)),
);

// -------------------------------------------------------------- children ----

final childrenProvider = FutureProvider<List<Child>>((ref) async {
  final auth = ref.watch(authProvider);
  if (auth is! AuthSignedIn || !auth.profile.isGuardian) return const [];

  return ref.watch(repositoryProvider).children();
});

/// Which child the parent is currently looking at.
///
/// Held app-wide rather than per screen: switching child on the dashboard must carry through
/// to attendance, grades and the timeline without re-selecting on each tab.
final selectedChildProvider = StateProvider<int?>((ref) => null);

/// Resolves the student whose data every screen should load.
final activeStudentIdProvider = Provider<int?>((ref) {
  final auth = ref.watch(authProvider);
  if (auth is! AuthSignedIn) return null;

  // A student is always looking at themselves.
  if (auth.profile.studentId != null) return auth.profile.studentId;

  final selected = ref.watch(selectedChildProvider);
  if (selected != null) return selected;

  // Default to the first approved child so a single-child parent never sees a picker.
  return ref.watch(childrenProvider).valueOrNull?.firstOrNull?.studentId;
});

// ------------------------------------------------------------- app data ----

final activityProvider = FutureProvider.family<
    ({List<ActivityEntry> timeline, Map<String, dynamic>? presence}), DateTime?>((ref, date) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).activity(studentId: studentId, date: date);
});

final attendanceProvider =
    FutureProvider<({AttendanceSummary summary, List<AttendanceDay> days})>((ref) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).attendance(studentId: studentId);
});

final timetableProvider = FutureProvider<List<TimetableEntry>>((ref) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).timetable(studentId: studentId);
});

final gradesProvider = FutureProvider<
    ({double overall, List<SubjectAverage> bySubject, List<GradeEntry> grades})>((ref) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).grades(studentId: studentId);
});

final assignmentsProvider = FutureProvider<List<AssignmentItem>>((ref) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).assignments(studentId: studentId);
});

final notificationsProvider = FutureProvider<List<AppNotification>>((ref) {
  return ref.watch(repositoryProvider).notifications();
});

final unreadCountProvider = FutureProvider<int>((ref) {
  return ref.watch(repositoryProvider).unreadCount();
});

final rfidStatusProvider = FutureProvider<Map<String, dynamic>>((ref) {
  final studentId = ref.watch(activeStudentIdProvider);
  return ref.watch(repositoryProvider).rfidStatus(studentId: studentId);
});
