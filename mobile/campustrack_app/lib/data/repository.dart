import 'dart:convert';

import '../core/api_client.dart';
import 'models.dart';

/// All server access for the app, in one place.
///
/// Screens depend on this, never on Dio. That keeps HTTP details out of the widget tree and
/// makes every screen testable by substituting a repository, which is exactly what the
/// widget tests do.
class CampusRepository {
  CampusRepository({required this.api, required this.storage});

  final ApiClient api;
  final TokenStorage storage;

  // ------------------------------------------------------------------ auth ----

  Future<UserProfile> signIn(String userNameOrEmail, String password) async {
    final data = await api.post<Map<String, dynamic>>('/auth/login', body: {
      'userNameOrEmail': userNameOrEmail,
      'password': password,
      'platform': 'Android',
      'deviceName': 'CampusTrack mobile',
    });

    final profile = UserProfile.fromJson(data['user'] as Map<String, dynamic>);

    await storage.save(
      accessToken: data['accessToken'] as String,
      refreshToken: data['refreshToken'] as String,
      profileJson: jsonEncode(profile.toJson()),
    );

    return profile;
  }

  Future<void> signOut() async {
    final refresh = await storage.refreshToken;

    try {
      // Best effort: the local session must be cleared even if the server is unreachable.
      if (refresh != null) await api.post<dynamic>('/auth/logout', body: {'refreshToken': refresh});
    } catch (_) {
      // deliberately ignored
    } finally {
      await storage.clear();
    }
  }

  /// The profile cached at sign-in, used to render the shell before the network responds.
  Future<UserProfile?> cachedProfile() async {
    final raw = await storage.cachedProfile;
    if (raw == null) return null;

    try {
      return UserProfile.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  Future<UserProfile> refreshProfile() async {
    final data = await api.get<Map<String, dynamic>>('/auth/me');
    final profile = UserProfile.fromJson(data);

    final access = await storage.accessToken;
    final refresh = await storage.refreshToken;
    if (access != null && refresh != null) {
      await storage.save(
        accessToken: access,
        refreshToken: refresh,
        profileJson: jsonEncode(profile.toJson()),
      );
    }

    return profile;
  }

  Future<void> changePassword(String current, String next) =>
      api.post<dynamic>('/auth/change-password',
          body: {'currentPassword': current, 'newPassword': next});

  Future<void> registerPushToken(String token) => api.post<dynamic>(
        '/auth/device-token',
        body: {'token': token, 'platform': 'Android', 'deviceName': 'CampusTrack mobile'},
      );

  // -------------------------------------------------------------- children ----

  Future<List<Child>> children() async {
    final data = await api.get<List<dynamic>>('/me/children');
    return data.map((e) => Child.fromJson(e as Map<String, dynamic>)).toList();
  }

  // ------------------------------------------------------------- dashboard ----

  Future<Map<String, dynamic>> guardianDashboard() =>
      api.get<Map<String, dynamic>>('/dashboard/parent');

  Future<Map<String, dynamic>> studentDashboard({int? studentId}) =>
      api.get<Map<String, dynamic>>('/dashboard/student', query: {'studentId': studentId});

  // -------------------------------------------------------------- activity ----

  /// A day's movement timeline. [studentId] is optional for a student and for a guardian
  /// with a single child; the API resolves it from the caller either way.
  Future<({List<ActivityEntry> timeline, Map<String, dynamic>? presence})> activity({
    int? studentId,
    DateTime? date,
  }) async {
    final data = await api.get<Map<String, dynamic>>('/me/activity', query: {
      'studentId': studentId,
      'date': date == null ? null : _dateParam(date),
    });

    final timeline = (data['timeline'] as List<dynamic>? ?? [])
        .map((e) => ActivityEntry.fromJson(e as Map<String, dynamic>))
        .toList();

    return (timeline: timeline, presence: data['presence'] as Map<String, dynamic>?);
  }

  Future<Map<String, dynamic>> dailyReport({int? studentId, DateTime? date}) =>
      api.get<Map<String, dynamic>>('/me/daily-report', query: {
        'studentId': studentId,
        'date': date == null ? null : _dateParam(date),
      });

  // ------------------------------------------------------------ attendance ----

  Future<({AttendanceSummary summary, List<AttendanceDay> days})> attendance({
    int? studentId,
    DateTime? from,
    DateTime? to,
  }) async {
    final data = await api.get<Map<String, dynamic>>('/me/attendance', query: {
      'studentId': studentId,
      'from': from == null ? null : _dateParam(from),
      'to': to == null ? null : _dateParam(to),
    });

    return (
      summary: AttendanceSummary.fromJson(data['summary'] as Map<String, dynamic>),
      days: (data['days'] as List<dynamic>? ?? [])
          .map((e) => AttendanceDay.fromJson(e as Map<String, dynamic>))
          .toList(),
    );
  }

  // ------------------------------------------------------------- timetable ----

  Future<List<TimetableEntry>> timetable({int? studentId}) async {
    final data = await api.get<List<dynamic>>('/me/timetable', query: {'studentId': studentId});
    return data.map((e) => TimetableEntry.fromJson(e as Map<String, dynamic>)).toList();
  }

  // ---------------------------------------------------------------- grades ----

  Future<({double overall, List<SubjectAverage> bySubject, List<GradeEntry> grades})> grades({
    int? studentId,
  }) async {
    final data = await api.get<Map<String, dynamic>>('/me/grades', query: {'studentId': studentId});

    return (
      overall: (data['overallPercentage'] as num? ?? 0).toDouble(),
      bySubject: (data['bySubject'] as List<dynamic>? ?? [])
          .map((e) => SubjectAverage.fromJson(e as Map<String, dynamic>))
          .toList(),
      grades: (data['grades'] as List<dynamic>? ?? [])
          .map((e) => GradeEntry.fromJson(e as Map<String, dynamic>))
          .toList(),
    );
  }

  // ----------------------------------------------------------- assignments ----

  Future<List<AssignmentItem>> assignments({int? studentId}) async {
    final data = await api.get<List<dynamic>>('/me/assignments', query: {'studentId': studentId});
    return data.map((e) => AssignmentItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<List<dynamic>> quizzes({int? studentId}) =>
      api.get<List<dynamic>>('/me/quizzes', query: {'studentId': studentId});

  Future<List<dynamic>> exams({int? studentId}) =>
      api.get<List<dynamic>>('/me/exams', query: {'studentId': studentId});

  // --------------------------------------------------------------- rfid ------

  Future<Map<String, dynamic>> rfidStatus({int? studentId}) =>
      api.get<Map<String, dynamic>>('/me/rfid-status', query: {'studentId': studentId});

  // --------------------------------------------------------- notifications ----

  Future<List<AppNotification>> notifications({int page = 1, int? studentId, bool unreadOnly = false}) async {
    final data = await api.get<Map<String, dynamic>>('/notifications', query: {
      'page': page,
      'pageSize': 30,
      'studentId': studentId,
      'unreadOnly': unreadOnly ? true : null,
    });

    return (data['items'] as List<dynamic>? ?? [])
        .map((e) => AppNotification.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<int> unreadCount() async {
    final data = await api.get<Map<String, dynamic>>('/notifications/unread-count');
    return data['count'] as int? ?? 0;
  }

  Future<void> markRead(int id) => api.post<dynamic>('/notifications/$id/read');
  Future<void> markAllRead() => api.post<dynamic>('/notifications/read-all');

  Future<List<dynamic>> notificationPreferences() =>
      api.get<List<dynamic>>('/notifications/preferences');

  Future<void> saveNotificationPreferences(List<Map<String, dynamic>> preferences) =>
      api.put<dynamic>('/notifications/preferences', body: preferences);

  // --------------------------------------------------------- announcements ----

  Future<List<dynamic>> announcements({int page = 1}) async {
    final data = await api.get<Map<String, dynamic>>('/me/announcements',
        query: {'page': page, 'pageSize': 20});
    return data['items'] as List<dynamic>? ?? [];
  }

  /// The API takes DateOnly, which serialises as yyyy-MM-dd.
  String _dateParam(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}';
}
