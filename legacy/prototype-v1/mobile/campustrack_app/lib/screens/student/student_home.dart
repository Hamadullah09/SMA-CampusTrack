import 'package:flutter/material.dart';
import '../../core/api_client.dart';
import '../../core/session.dart';
import '../login_screen.dart';
import '../shared/notifications_tab.dart';
import '../shared/schedule_tab.dart';
import 'assignments_tab.dart';
import 'uploads_tab.dart';

/// Student portal: semester schedule, assignments & notes (QR download),
/// project/thesis uploads, notifications.
class StudentHome extends StatefulWidget {
  const StudentHome({super.key});
  @override
  State<StudentHome> createState() => _StudentHomeState();
}

class _StudentHomeState extends State<StudentHome> {
  int _tab = 0;
  Map<String, dynamic>? _profile;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    try {
      final data = await Api.get('/api/me/student');
      setState(() => _profile = data);
    } catch (_) {}
  }

  Future<void> _logout() async {
    await Session.clear();
    if (!mounted) return;
    Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const LoginScreen()));
  }

  @override
  Widget build(BuildContext context) {
    final studentId = Session.studentId;
    if (studentId == null) {
      return const Scaffold(
          body: Center(child: Text('No student profile linked.')));
    }

    final tabs = [
      ScheduleTab(studentId: studentId),
      AssignmentsTab(studentId: studentId),
      UploadsTab(studentId: studentId),
      const NotificationsTab(),
    ];

    return Scaffold(
      appBar: AppBar(
        title: Text(_profile == null
            ? 'CampusTrack'
            : '${_profile!['name']} • ${_profile!['section'] ?? ''}'),
        actions: [
          IconButton(icon: const Icon(Icons.logout), onPressed: _logout)
        ],
      ),
      body: tabs[_tab],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (i) => setState(() => _tab = i),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.calendar_month), label: 'Schedule'),
          NavigationDestination(icon: Icon(Icons.assignment), label: 'Assignments'),
          NavigationDestination(icon: Icon(Icons.upload_file), label: 'My work'),
          NavigationDestination(icon: Icon(Icons.notifications), label: 'Alerts'),
        ],
      ),
    );
  }
}
