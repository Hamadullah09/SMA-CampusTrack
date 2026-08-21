import 'package:flutter/material.dart';
import '../../core/api_client.dart';
import '../../core/session.dart';
import '../login_screen.dart';
import '../shared/activity_tab.dart';
import '../shared/attendance_tab.dart';
import '../shared/notifications_tab.dart';
import '../shared/schedule_tab.dart';
import 'feedback_tab.dart';

/// Parent portal: pick a child, then attendance / schedule / activity /
/// feedback / notifications.
class ParentHome extends StatefulWidget {
  const ParentHome({super.key});
  @override
  State<ParentHome> createState() => _ParentHomeState();
}

class _ParentHomeState extends State<ParentHome> {
  List<dynamic>? _children;
  int? _selectedChildId;
  int _tab = 0;
  String? _error;

  @override
  void initState() { super.initState(); _loadChildren(); }

  Future<void> _loadChildren() async {
    try {
      final data = await Api.get('/api/me/children');
      setState(() {
        _children = data;
        if (data.isNotEmpty) _selectedChildId = data.first['id'];
      });
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  Future<void> _logout() async {
    await Session.clear();
    if (!mounted) return;
    Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const LoginScreen()));
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Scaffold(body: Center(child: Text(_error!)));
    }
    if (_children == null) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    Map<String, dynamic>? selected;
    if (_selectedChildId != null) {
      selected = Map<String, dynamic>.from(
          _children!.firstWhere((c) => c['id'] == _selectedChildId));
    }

    final tabs = _selectedChildId == null
        ? [const Center(child: Text('No student linked to this account.'))]
        : [
            AttendanceTab(studentId: _selectedChildId!),
            ScheduleTab(studentId: _selectedChildId!),
            ActivityTab(studentId: _selectedChildId!),
            FeedbackTab(children: _children!),
            const NotificationsTab(),
          ];

    return Scaffold(
      appBar: AppBar(
        title: Text('Hi, ${Session.fullName ?? 'Parent'}'),
        actions: [
          if (_children!.length > 1)
            DropdownButton<int>(
              value: _selectedChildId,
              underline: const SizedBox(),
              items: _children!
                  .map<DropdownMenuItem<int>>((c) => DropdownMenuItem(
                      value: c['id'], child: Text(c['name'])))
                  .toList(),
              onChanged: (v) => setState(() => _selectedChildId = v),
            ),
          IconButton(icon: const Icon(Icons.logout), onPressed: _logout),
        ],
        bottom: selected == null
            ? null
            : PreferredSize(
                preferredSize: const Size.fromHeight(24),
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Text(
                    '${selected['name']} • ${selected['section'] ?? 'no section'} • ${selected['regNo']}',
                    style: const TextStyle(fontSize: 12),
                  ),
                ),
              ),
      ),
      body: tabs[_tab.clamp(0, tabs.length - 1)],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (i) => setState(() => _tab = i),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.badge), label: 'Attendance'),
          NavigationDestination(icon: Icon(Icons.calendar_month), label: 'Schedule'),
          NavigationDestination(icon: Icon(Icons.star), label: 'Activity'),
          NavigationDestination(icon: Icon(Icons.feedback), label: 'Feedback'),
          NavigationDestination(icon: Icon(Icons.notifications), label: 'Alerts'),
        ],
      ),
    );
  }
}
