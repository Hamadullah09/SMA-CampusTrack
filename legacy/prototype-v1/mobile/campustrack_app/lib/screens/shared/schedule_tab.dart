import 'package:flutter/material.dart';
import '../../core/api_client.dart';

/// Full-semester weekly timetable for one student, grouped by day.
class ScheduleTab extends StatefulWidget {
  final int studentId;
  const ScheduleTab({super.key, required this.studentId});

  @override
  State<ScheduleTab> createState() => _ScheduleTabState();
}

class _ScheduleTabState extends State<ScheduleTab> {
  List<dynamic>? _entries;
  String? _error;

  static const _days = ['', 'Monday', 'Tuesday', 'Wednesday', 'Thursday',
                        'Friday', 'Saturday', 'Sunday'];

  @override
  void initState() { super.initState(); _load(); }

  @override
  void didUpdateWidget(ScheduleTab old) {
    super.didUpdateWidget(old);
    if (old.studentId != widget.studentId) _load();
  }

  Future<void> _load() async {
    setState(() { _entries = null; _error = null; });
    try {
      final data = await Api.get('/api/schedule/student/${widget.studentId}');
      setState(() => _entries = data);
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) return Center(child: Text(_error!));
    if (_entries == null) return const Center(child: CircularProgressIndicator());
    if (_entries!.isEmpty) {
      return const Center(child: Text('No timetable published yet.'));
    }

    final byDay = <int, List<dynamic>>{};
    for (final e in _entries!) {
      byDay.putIfAbsent(e['dayOfWeek'] as int, () => []).add(e);
    }
    final days = byDay.keys.toList()..sort();

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(12),
        children: [
          if (_entries!.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text('Semester: ${_entries!.first['semester']}',
                  style: Theme.of(context).textTheme.titleSmall),
            ),
          for (final d in days) ...[
            Padding(
              padding: const EdgeInsets.fromLTRB(4, 12, 4, 4),
              child: Text(_days[d],
                  style: Theme.of(context)
                      .textTheme
                      .titleMedium
                      ?.copyWith(fontWeight: FontWeight.bold)),
            ),
            ...byDay[d]!.map((e) => Card(
                  child: ListTile(
                    leading: Text('${e['startTime']}\n${e['endTime']}',
                        textAlign: TextAlign.center,
                        style: const TextStyle(fontSize: 12)),
                    title: Text(e['subject']),
                    subtitle: Text([
                      if (e['teacher'] != null) e['teacher'],
                      if (e['room'] != null) e['room'],
                    ].join(' • ')),
                  ),
                )),
          ],
        ],
      ),
    );
  }
}
