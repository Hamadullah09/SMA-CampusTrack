import 'package:flutter_test/flutter_test.dart';

import 'package:campustrack_app/data/models.dart';

/// Tests for the profile the whole app routes on.
///
/// `primaryPortal` and the two role getters decide which portal a person lands in and
/// therefore whose data they can reach, so the parsing that produces them is worth pinning
/// down. These use no platform channels, which keeps them runnable in CI without a device.
void main() {
  group('UserProfile.fromJson', () {
    test('reads a guardian the API returned in full', () {
      final profile = UserProfile.fromJson(const {
        'id': 3,
        'userName': 'fatima.ali',
        'fullName': 'Fatima Ali',
        'email': 'fatima@example.com',
        'roles': ['Guardian'],
        'guardianId': 1,
        'primaryPortal': 'parent',
        'schoolName': 'SMA Demonstration School',
      });

      expect(profile.id, 3);
      expect(profile.userName, 'fatima.ali');
      expect(profile.primaryPortal, 'parent');
      expect(profile.isGuardian, isTrue);
      expect(profile.isStudent, isFalse);
      expect(profile.studentId, isNull);
    });

    test('treats a linked profile id as authoritative, not just the role name', () {
      // A student whose role list has not been populated is still a student: the id is
      // what the API scopes their data by, so the getter must not depend on the label.
      final profile = UserProfile.fromJson(const {
        'id': 2,
        'userName': 'ahmed.ali',
        'fullName': 'Ahmed Ali',
        'roles': <String>[],
        'studentId': 1,
        'primaryPortal': 'student',
      });

      expect(profile.isStudent, isTrue);
      expect(profile.isGuardian, isFalse);
    });

    test('falls back rather than throwing when optional fields are absent', () {
      // Older builds of the app must survive a response that omits fields added later.
      final profile = UserProfile.fromJson(const {'id': 9});

      expect(profile.userName, isEmpty);
      expect(profile.roles, isEmpty);
      expect(profile.primaryPortal, 'student');
      expect(profile.mustChangePassword, isFalse);
    });

    test('survives a round trip through toJson', () {
      const original = UserProfile(
        id: 5,
        userName: 'imran.hossain',
        fullName: 'Imran Hossain',
        roles: ['Teacher'],
        primaryPortal: 'teacher',
        mustChangePassword: true,
      );

      // The profile is cached as JSON in secure storage and read back on launch, so a
      // lossy round trip would quietly sign someone into the wrong portal.
      final restored = UserProfile.fromJson(original.toJson());

      expect(restored.id, original.id);
      expect(restored.userName, original.userName);
      expect(restored.roles, original.roles);
      expect(restored.primaryPortal, original.primaryPortal);
      expect(restored.mustChangePassword, isTrue);
    });
  });
}
