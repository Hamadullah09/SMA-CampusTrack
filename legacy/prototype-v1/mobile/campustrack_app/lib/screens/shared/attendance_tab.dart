import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

/// Two views: day-by-day gate attendance and the full room-by-room
/// movement timeline recorded by the RFID readers.
class AttendanceTab extends StatefulWidget {
  final int studentId;
  const AttendanceTab({super.key, required this.studentId});

  @override
  State<AttendanceTab> createState() => _AttendanceTabState();
}

class _AttendanceTabState extends State<AttendanceTab> {
  List<dynamic>? _daily;
  List<dynamic>? _timeline;
  String? _error;
  bool _showTimeline = false;

  @override
  void initState() { super.initState(); _load(); }

  @override
  void didUpdateWidget(AttendanceTab old) {
    super.didUpdateWidget(old);
    if (old.studentId != widget.studentId) _load();
  }

  Future<void> _load() async {
    setState(() { _daily = _timeline = null; _error = null; });
    try {
      final results = await Future.wait([
        Api.get('/api/attendance/student/${widget.studentId}/daily'),
        Api.get('/api/attendance/student/${widget.studentId}'),
      ]);
      setState(() { _daily = results[0]; _timeline = results[1]; });
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) return Center(child: Text(_error!));
    if (_daily == null) return const Center(child: CircularProgressIndicator());

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(8),
          child: SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: false, label: Text('Daily'), icon: Icon(Icons.calendar_month)),
              ButtonSegment(value: true, label: Text('Movements'), icon: Icon(Icons.timeline)),
            ],
            selected: {_showTimeline},
            onSelectionChanged: (s) => setState(() => _showTimeline = s.first),
          ),
        ),
        Expanded(
          child: RefreshIndicator(
            onRefresh: _load,
            child: _showTimeline ? _buildTimeline() : _buildDaily(),
          ),
        ),
      ],
    );
  }

  Widget _buildDaily() {
    if (_daily!.isEmpty) return const Center(child: Text('No attendance recorded yet.'));
    final timeFmt = DateFormat('hh:mm a');
    final dateFmt = DateFormat('EEE, dd MMM');
    return ListView.builder(
      itemCount: _daily!.length,
      itemBuilder: (_, i) {
        final d = _daily![i];
        final arrival = d['arrival'] == null
            ? '—' : timeFmt.format(DateTime.parse(d['arrival']).toLocal());
        final departure = d['departure'] == null
            ? '—' : timeFmt.format(DateTime.parse(d['departure']).toLocal());
        return Card(
          margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
          child: ListTile(
            leading: Icon(
                d['present'] ? Icons.check_circle : Icons.cancel,
                color: d['present'] ? Colors.green : Colors.red),
            title: Text(dateFmt.format(DateTime.parse(d['date']))),
            subtitle: Text('In: $arrival    Out: $departure'),
          ),
        );
      },
    );
  }

  Widget _buildTimeline() {
    if (_timeline!.isEmpty) return const Center(child: Text('No movements recorded.'));
    final fmt = DateFormat('dd MMM, hh:mm a');
    return ListView.builder(
      itemCount: _timeline!.length,
      itemBuilder: (_, i) {
        final e = _timeline![i];
        final entry = e['direction'] == 'Entry';
        return ListTile(
          dense: true,
          leading: Icon(entry ? Icons.arrow_forward : Icons.arrow_back,
              color: entry ? Colors.green : Colors.orange),
          title: Text('${entry ? 'Entered' : 'Left'} ${e['room']}'),
          subtitle: Text(
              '${e['roomType']} • ${fmt.format(DateTime.parse(e['eventTime']).toLocal())}'),
        );
      },
    );
  }
}
