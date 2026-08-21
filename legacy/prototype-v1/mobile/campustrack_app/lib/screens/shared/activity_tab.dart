import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

/// Teacher-entered progress & activity reports for one student.
class ActivityTab extends StatefulWidget {
  final int studentId;
  const ActivityTab({super.key, required this.studentId});

  @override
  State<ActivityTab> createState() => _ActivityTabState();
}

class _ActivityTabState extends State<ActivityTab> {
  List<dynamic>? _reports;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  @override
  void didUpdateWidget(ActivityTab old) {
    super.didUpdateWidget(old);
    if (old.studentId != widget.studentId) _load();
  }

  Future<void> _load() async {
    setState(() { _reports = null; _error = null; });
    try {
      final data = await Api.get('/api/activity/student/${widget.studentId}');
      setState(() => _reports = data);
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  Color _color(String category) => switch (category) {
        'Academic' => Colors.blue,
        'Behaviour' => Colors.purple,
        'Sports' => Colors.teal,
        'TestResult' => Colors.indigo,
        'HomeworkStatus' => Colors.brown,
        _ => Colors.grey,
      };

  @override
  Widget build(BuildContext context) {
    if (_error != null) return Center(child: Text(_error!));
    if (_reports == null) return const Center(child: CircularProgressIndicator());
    if (_reports!.isEmpty) return const Center(child: Text('No reports yet.'));

    final fmt = DateFormat('dd MMM yyyy');
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.builder(
        itemCount: _reports!.length,
        itemBuilder: (_, i) {
          final r = _reports![i];
          return Card(
            margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
            child: ListTile(
              leading: CircleAvatar(
                backgroundColor: _color(r['category']).withOpacity(.15),
                child: Text(r['grade'] ?? '•',
                    style: TextStyle(
                        color: _color(r['category']),
                        fontWeight: FontWeight.bold, fontSize: 12)),
              ),
              title: Text(r['title']),
              subtitle: Text([
                '${r['category']} • ${fmt.format(DateTime.parse(r['reportDate']))}',
                if (r['remarks'] != null) r['remarks'],
                'By ${r['teacher']}',
              ].join('\n')),
              isThreeLine: true,
            ),
          );
        },
      ),
    );
  }
}
