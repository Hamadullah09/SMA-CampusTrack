/// Typed models for the API payloads the app consumes.
///
/// Hand-written rather than generated: the surface is small, and doing it by hand keeps
/// null-handling explicit at the boundary where the server's optional fields become the
/// app's optional fields.
library;

class UserProfile {
  const UserProfile({
    required this.id,
    required this.userName,
    required this.fullName,
    required this.roles,
    required this.primaryPortal,
    this.email,
    this.phoneNumber,
    this.studentId,
    this.guardianId,
    this.mustChangePassword = false,
    this.schoolName,
  });

  final int id;
  final String userName;
  final String fullName;
  final String? email;
  final String? phoneNumber;
  final List<String> roles;
  final int? studentId;
  final int? guardianId;
  final String primaryPortal;
  final bool mustChangePassword;
  final String? schoolName;

  bool get isGuardian => guardianId != null || roles.contains('Guardian');
  bool get isStudent => studentId != null || roles.contains('Student');

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        id: json['id'] as int,
        userName: json['userName'] as String? ?? '',
        fullName: json['fullName'] as String? ?? '',
        email: json['email'] as String?,
        phoneNumber: json['phoneNumber'] as String?,
        roles: (json['roles'] as List<dynamic>? ?? []).cast<String>(),
        studentId: json['studentId'] as int?,
        guardianId: json['guardianId'] as int?,
        primaryPortal: json['primaryPortal'] as String? ?? 'student',
        mustChangePassword: json['mustChangePassword'] as bool? ?? false,
        schoolName: json['schoolName'] as String?,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'userName': userName,
        'fullName': fullName,
        'email': email,
        'phoneNumber': phoneNumber,
        'roles': roles,
        'studentId': studentId,
        'guardianId': guardianId,
        'primaryPortal': primaryPortal,
        'mustChangePassword': mustChangePassword,
        'schoolName': schoolName,
      };
}

/// A child a guardian is approved to follow.
class Child {
  const Child({
    required this.studentId,
    required this.name,
    required this.firstName,
    required this.studentCode,
    required this.presenceState,
    required this.canViewAcademics,
    required this.hasActiveCard,
    this.sectionName,
    this.className,
    this.photoUrl,
    this.relationship,
  });

  final int studentId;
  final String name;
  final String firstName;
  final String studentCode;
  final String? sectionName;
  final String? className;
  final String? photoUrl;
  final String? relationship;
  final String presenceState;
  final bool canViewAcademics;
  final bool hasActiveCard;

  /// What a parent glancing at the app wants to know first.
  String get presenceLabel => switch (presenceState) {
        'OnCampus' => 'At school',
        'InRoom' => 'In class',
        _ => 'Not at school',
      };

  bool get isOnSite => presenceState != 'Outside';

  factory Child.fromJson(Map<String, dynamic> json) => Child(
        studentId: json['studentId'] as int,
        name: json['name'] as String? ?? '',
        firstName: json['firstName'] as String? ?? '',
        studentCode: json['studentCode'] as String? ?? '',
        sectionName: json['sectionName'] as String?,
        className: json['className'] as String?,
        photoUrl: json['photoUrl'] as String?,
        relationship: json['relationship'] as String?,
        presenceState: json['presenceState'] as String? ?? 'Outside',
        canViewAcademics: json['canViewAcademics'] as bool? ?? true,
        hasActiveCard: json['hasActiveCard'] as bool? ?? false,
      );
}

/// One entry in a student's day.
class ActivityEntry {
  const ActivityEntry({
    required this.occurredAtUtc,
    required this.title,
    this.detail,
    this.locationName,
    this.subjectName,
    this.eventType,
    this.icon,
    this.durationMinutes,
  });

  final DateTime occurredAtUtc;
  final String title;
  final String? detail;
  final String? locationName;
  final String? subjectName;
  final String? eventType;
  final String? icon;
  final int? durationMinutes;

  bool get isArrival => eventType == 'SchoolEntry';
  bool get isDeparture => eventType == 'SchoolExit';

  factory ActivityEntry.fromJson(Map<String, dynamic> json) => ActivityEntry(
        occurredAtUtc: DateTime.parse(json['occurredAtUtc'] as String).toUtc(),
        title: json['title'] as String? ?? '',
        detail: json['detail'] as String?,
        locationName: json['locationName'] as String?,
        subjectName: json['subjectName'] as String?,
        eventType: json['eventType'] as String?,
        icon: json['icon'] as String?,
        durationMinutes: json['durationMinutes'] as int?,
      );
}

class AttendanceSummary {
  const AttendanceSummary({
    required this.totalDays,
    required this.presentDays,
    required this.absentDays,
    required this.lateDays,
    required this.attendancePercentage,
    required this.isBelowRequirement,
  });

  final int totalDays;
  final int presentDays;
  final int absentDays;
  final int lateDays;
  final double attendancePercentage;
  final bool isBelowRequirement;

  factory AttendanceSummary.fromJson(Map<String, dynamic> json) => AttendanceSummary(
        totalDays: json['totalDays'] as int? ?? 0,
        presentDays: json['presentDays'] as int? ?? 0,
        absentDays: json['absentDays'] as int? ?? 0,
        lateDays: json['lateDays'] as int? ?? 0,
        attendancePercentage: (json['attendancePercentage'] as num? ?? 0).toDouble(),
        isBelowRequirement: json['isBelowRequirement'] as bool? ?? false,
      );
}

class AttendanceDay {
  const AttendanceDay({
    required this.date,
    required this.status,
    this.firstEntryAtUtc,
    this.lastExitAtUtc,
    this.lateMinutes = 0,
    this.remarks,
  });

  final DateTime date;
  final String status;
  final DateTime? firstEntryAtUtc;
  final DateTime? lastExitAtUtc;
  final int lateMinutes;
  final String? remarks;

  factory AttendanceDay.fromJson(Map<String, dynamic> json) => AttendanceDay(
        date: DateTime.parse(json['date'] as String),
        status: json['status'] as String? ?? 'NotRecorded',
        firstEntryAtUtc: _parseUtc(json['firstEntryAtUtc']),
        lastExitAtUtc: _parseUtc(json['lastExitAtUtc']),
        lateMinutes: json['lateMinutes'] as int? ?? 0,
        remarks: json['remarks'] as String?,
      );
}

class TimetableEntry {
  const TimetableEntry({
    required this.id,
    required this.dayOfWeek,
    required this.dayName,
    required this.startTime,
    required this.endTime,
    required this.subjectName,
    this.teacherName,
    this.classroomName,
    this.subjectColour,
    this.isBreak = false,
  });

  final int id;
  final int dayOfWeek;
  final String dayName;
  final String startTime;
  final String endTime;
  final String subjectName;
  final String? teacherName;
  final String? classroomName;
  final String? subjectColour;
  final bool isBreak;

  /// "08:00:00" from the API becomes "8:00" for display.
  String get displayStart => _shortTime(startTime);
  String get displayEnd => _shortTime(endTime);

  factory TimetableEntry.fromJson(Map<String, dynamic> json) => TimetableEntry(
        id: json['id'] as int,
        dayOfWeek: json['dayOfWeek'] as int? ?? 1,
        dayName: json['dayName'] as String? ?? '',
        startTime: json['startTime'] as String? ?? '00:00:00',
        endTime: json['endTime'] as String? ?? '00:00:00',
        subjectName: json['subjectName'] as String? ?? '',
        teacherName: json['teacherName'] as String?,
        classroomName: json['classroomName'] as String?,
        subjectColour: json['subjectColour'] as String?,
        isBreak: json['isBreak'] as bool? ?? false,
      );
}

class GradeEntry {
  const GradeEntry({
    required this.id,
    required this.title,
    required this.subjectName,
    required this.score,
    required this.maxScore,
    required this.percentage,
    required this.recordedOn,
    this.letter,
    this.category,
    this.remarks,
  });

  final int id;
  final String title;
  final String subjectName;
  final double score;
  final double maxScore;
  final double percentage;
  final DateTime recordedOn;
  final String? letter;
  final String? category;
  final String? remarks;

  factory GradeEntry.fromJson(Map<String, dynamic> json) => GradeEntry(
        id: json['id'] as int,
        title: json['title'] as String? ?? '',
        subjectName: json['subjectName'] as String? ?? '',
        score: (json['score'] as num? ?? 0).toDouble(),
        maxScore: (json['maxScore'] as num? ?? 100).toDouble(),
        percentage: (json['percentage'] as num? ?? 0).toDouble(),
        recordedOn: DateTime.parse(json['recordedOn'] as String),
        letter: json['letter'] as String?,
        category: json['category'] as String?,
        remarks: json['remarks'] as String?,
      );
}

class SubjectAverage {
  const SubjectAverage({
    required this.subject,
    required this.count,
    required this.average,
    this.colour,
  });

  final String subject;
  final int count;
  final double average;
  final String? colour;

  factory SubjectAverage.fromJson(Map<String, dynamic> json) => SubjectAverage(
        subject: json['subject'] as String? ?? '',
        count: json['count'] as int? ?? 0,
        average: (json['average'] as num? ?? 0).toDouble(),
        colour: json['colour'] as String?,
      );
}

class AppNotification {
  const AppNotification({
    required this.id,
    required this.category,
    required this.title,
    required this.body,
    required this.isRead,
    required this.createdAtUtc,
    this.priority,
    this.studentId,
  });

  final int id;
  final String category;
  final String title;
  final String body;
  final bool isRead;
  final DateTime createdAtUtc;
  final String? priority;
  final int? studentId;

  bool get isUrgent => priority == 'High' || priority == 'Critical';

  factory AppNotification.fromJson(Map<String, dynamic> json) => AppNotification(
        id: json['id'] as int,
        category: json['category'] as String? ?? 'System',
        title: json['title'] as String? ?? '',
        body: json['body'] as String? ?? '',
        isRead: json['isRead'] as bool? ?? false,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toUtc(),
        priority: json['priority'] as String?,
        studentId: json['studentId'] as int?,
      );
}

class AssignmentItem {
  const AssignmentItem({
    required this.id,
    required this.title,
    required this.subjectName,
    required this.dueAtUtc,
    required this.maxScore,
    this.instructions,
    this.teacherName,
    this.submissionStatus,
    this.score,
    this.feedback,
  });

  final int id;
  final String title;
  final String subjectName;
  final DateTime dueAtUtc;
  final double maxScore;
  final String? instructions;
  final String? teacherName;
  final String? submissionStatus;
  final double? score;
  final String? feedback;

  bool get isSubmitted =>
      submissionStatus != null && submissionStatus != 'NotSubmitted';
  bool get isOverdue => !isSubmitted && DateTime.now().toUtc().isAfter(dueAtUtc);

  factory AssignmentItem.fromJson(Map<String, dynamic> json) {
    final submission = json['submission'] as Map<String, dynamic>?;

    return AssignmentItem(
      id: json['id'] as int,
      title: json['title'] as String? ?? '',
      subjectName: json['subjectName'] as String? ?? '',
      dueAtUtc: DateTime.parse(json['dueAtUtc'] as String).toUtc(),
      maxScore: (json['maxScore'] as num? ?? 100).toDouble(),
      instructions: json['instructions'] as String?,
      teacherName: json['teacherName'] as String?,
      submissionStatus: submission?['status'] as String?,
      score: (submission?['score'] as num?)?.toDouble(),
      feedback: submission?['feedback'] as String?,
    );
  }
}

DateTime? _parseUtc(Object? value) =>
    value is String ? DateTime.parse(value).toUtc() : null;

String _shortTime(String raw) {
  final parts = raw.split(':');
  if (parts.length < 2) return raw;

  final hour = int.tryParse(parts[0]) ?? 0;
  final minute = parts[1];
  final suffix = hour >= 12 ? 'pm' : 'am';
  final display = hour % 12 == 0 ? 12 : hour % 12;

  return '$display:$minute$suffix';
}
