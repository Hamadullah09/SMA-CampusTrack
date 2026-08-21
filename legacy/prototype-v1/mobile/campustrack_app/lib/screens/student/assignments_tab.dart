import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../core/api_client.dart';
import 'qr_scan_screen.dart';

/// Assignments & notes for the student's section. Each row downloads the
/// file directly; the floating button opens the QR scanner so a code
/// printed on a handout / shown in class downloads the same file.
class AssignmentsTab extends StatefulWidget {
  final int studentId;
  const AssignmentsTab({super.key, required this.studentId});

  @override
  State<AssignmentsTab> createState() => _AssignmentsTabState();
}

class _AssignmentsTabState extends State<AssignmentsTab> {
  List<dynamic>? _items;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _items = null; _error = null; });
    try {
      final data = await Api.get('/api/assignments/student/${widget.studentId}');
      setState(() => _items = data);
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  Future<void> _download(dynamic a) async {
    final url = Uri.parse('$kApiBaseUrl/api/assignments/download/${a['qrToken']}');
    if (!await launchUrl(url, mode: LaunchMode.externalApplication)) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Could not open download')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final fmt = DateFormat('dd MMM');
    return Scaffold(
      body: _error != null
          ? Center(child: Text(_error!))
          : _items == null
              ? const Center(child: CircularProgressIndicator())
              : _items!.isEmpty
                  ? const Center(child: Text('No assignments or notes yet.'))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.builder(
                        itemCount: _items!.length,
                        itemBuilder: (_, i) {
                          final a = _items![i];
                          return Card(
                            margin: const EdgeInsets.symmetric(
                                horizontal: 12, vertical: 4),
                            child: ListTile(
                              leading: Icon(a['docType'] == 'Notes'
                                  ? Icons.menu_book
                                  : Icons.assignment),
                              title: Text(a['title']),
                              subtitle: Text([
                                a['docType'],
                                if (a['teacher'] != null) a['teacher'],
                                if (a['dueDate'] != null)
                                  'Due ${fmt.format(DateTime.parse(a['dueDate']))}',
                              ].join(' • ')),
                              trailing: IconButton(
                                icon: const Icon(Icons.download),
                                onPressed: () => _download(a),
                              ),
                            ),
                          );
                        },
                      ),
                    ),
      floatingActionButton: FloatingActionButton.extended(
        icon: const Icon(Icons.qr_code_scanner),
        label: const Text('Scan QR'),
        onPressed: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => const QrScanScreen())),
      ),
    );
  }
}
