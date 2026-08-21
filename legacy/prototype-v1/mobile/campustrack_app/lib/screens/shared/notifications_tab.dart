import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

class NotificationsTab extends StatefulWidget {
  const NotificationsTab({super.key});
  @override
  State<NotificationsTab> createState() => _NotificationsTabState();
}

class _NotificationsTabState extends State<NotificationsTab> {
  List<dynamic>? _items;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    try {
      final data = await Api.get('/api/notifications');
      setState(() => _items = data);
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  IconData _icon(String type) => switch (type) {
        'GateEntry' => Icons.login,
        'GateExit' => Icons.logout,
        'DailySummary' => Icons.today,
        'WeeklySummary' => Icons.date_range,
        'Activity' => Icons.star,
        'FeedbackReply' => Icons.reply,
        'Assignment' => Icons.assignment,
        _ => Icons.notifications,
      };

  @override
  Widget build(BuildContext context) {
    if (_error != null) return Center(child: Text(_error!));
    if (_items == null) return const Center(child: CircularProgressIndicator());
    if (_items!.isEmpty) return const Center(child: Text('No notifications yet.'));

    final fmt = DateFormat('dd MMM, hh:mm a');
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.builder(
        itemCount: _items!.length,
        itemBuilder: (_, i) {
          final n = _items![i];
          return Card(
            margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
            child: ListTile(
              leading: Icon(_icon(n['notifType']),
                  color: n['isRead'] ? Colors.grey : const Color(0xFF1A56A8)),
              title: Text(n['title'],
                  style: TextStyle(
                      fontWeight:
                          n['isRead'] ? FontWeight.normal : FontWeight.bold)),
              subtitle: Text(
                  '${n['body']}\n${fmt.format(DateTime.parse(n['createdAt']).toLocal())}'),
              isThreeLine: true,
              onTap: () async {
                if (!n['isRead']) {
                  await Api.post('/api/notifications/${n['id']}/read');
                  _load();
                }
              },
            ),
          );
        },
      ),
    );
  }
}
