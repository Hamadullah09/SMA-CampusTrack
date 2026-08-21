import 'package:shared_preferences/shared_preferences.dart';

/// Holds the signed-in user's identity, persisted across app restarts.
class Session {
  static String? token;
  static String? role; // Parent | Student
  static String? fullName;
  static int? studentId;
  static int? parentId;

  static Future<void> load() async {
    final p = await SharedPreferences.getInstance();
    token = p.getString('token');
    role = p.getString('role');
    fullName = p.getString('fullName');
    studentId = p.getInt('studentId');
    parentId = p.getInt('parentId');
  }

  static Future<void> save(Map<String, dynamic> login) async {
    token = login['token'];
    role = login['role'];
    fullName = login['fullName'];
    studentId = login['studentId'];
    parentId = login['parentId'];
    final p = await SharedPreferences.getInstance();
    await p.setString('token', token!);
    await p.setString('role', role!);
    await p.setString('fullName', fullName ?? '');
    if (studentId != null) await p.setInt('studentId', studentId!);
    if (parentId != null) await p.setInt('parentId', parentId!);
  }

  static Future<void> clear() async {
    token = role = fullName = null;
    studentId = parentId = null;
    final p = await SharedPreferences.getInstance();
    await p.clear();
  }

  static bool get isLoggedIn => token != null;
}
