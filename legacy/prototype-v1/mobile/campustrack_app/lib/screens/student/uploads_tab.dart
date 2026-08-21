import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

/// Students upload projects, activity work and theses, and track the
/// review status / teacher remarks.
class UploadsTab extends StatefulWidget {
  final int studentId;
  const UploadsTab({super.key, required this.studentId});

  @override
  State<UploadsTab> createState() => _UploadsTabState();
}

class _UploadsTabState extends State<UploadsTab> {
  List<dynamic>? _items;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _items = null; _error = null; });
    try {
      final data = await Api.get('/api/uploads/student/${widget.studentId}');
      setState(() => _items = data);
    } catch (e) {
      setState(() => _error = e.toString());
    }
  }

  Future<void> _newUpload() async {
    final titleCtl = TextEditingController();
    final descCtl = TextEditingController();
    String type = 'Project';
    PlatformFile? picked;

    final submitted = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setDlg) => AlertDialog(
          title: const Text('Upload work'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  value: type,
                  decoration: const InputDecoration(labelText: 'Type'),
                  items: const [
                    DropdownMenuItem(value: 'Project', child: Text('Project')),
                    DropdownMenuItem(value: 'Activity', child: Text('Activity')),
                    DropdownMenuItem(value: 'Thesis', child: Text('Thesis')),
                  ],
                  onChanged: (v) => setDlg(() => type = v!),
                ),
                TextField(
                    controller: titleCtl,
                    decoration: const InputDecoration(labelText: 'Title')),
                TextField(
                    controller: descCtl,
                    decoration:
                        const InputDecoration(labelText: 'Description'),
                    maxLines: 2),
                const SizedBox(height: 12),
                OutlinedButton.icon(
                  icon: const Icon(Icons.attach_file),
                  label: Text(picked?.name ?? 'Choose file'),
                  onPressed: () async {
                    final result = await FilePicker.platform.pickFiles();
                    if (result != null) {
                      setDlg(() => picked = result.files.single);
                    }
                  },
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Cancel')),
            FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Upload')),
          ],
        ),
      ),
    );

    if (submitted != true || picked?.path == null || titleCtl.text.trim().isEmpty) {
      return;
    }
    try {
      await Api.upload(
        '/api/uploads',
        {
          'uploadType': type,
          'title': titleCtl.text.trim(),
          'description': descCtl.text.trim(),
        },
        'file',
        picked!.path!,
      );
      _load();
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Uploaded!')));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.toString())));
      }
    }
  }

  Color _statusColor(String s) => switch (s) {
        'Approved' => Colors.green,
        'Rejected' => Colors.red,
        'Reviewed' => Colors.orange,
        _ => Colors.grey,
      };

  @override
  Widget build(BuildContext context) {
    final fmt = DateFormat('dd MMM yyyy');
    return Scaffold(
      body: _error != null
          ? Center(child: Text(_error!))
          : _items == null
              ? const Center(child: CircularProgressIndicator())
              : _items!.isEmpty
                  ? const Center(
                      child: Text('Nothing uploaded yet.\nTap + to add your work.',
                          textAlign: TextAlign.center))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.builder(
                        itemCount: _items!.length,
                        itemBuilder: (_, i) {
                          final u = _items![i];
                          return Card(
                            margin: const EdgeInsets.symmetric(
                                horizontal: 12, vertical: 4),
                            child: ListTile(
                              leading: const Icon(Icons.description),
                              title: Text(u['title']),
                              subtitle: Text([
                                '${u['uploadType']} • ${fmt.format(DateTime.parse(u['uploadedAt']).toLocal())}',
                                if (u['teacherRemarks'] != null)
                                  'Remarks: ${u['teacherRemarks']}',
                              ].join('\n')),
                              trailing: Chip(
                                label: Text(u['status'],
                                    style: const TextStyle(
                                        fontSize: 11, color: Colors.white)),
                                backgroundColor: _statusColor(u['status']),
                              ),
                            ),
                          );
                        },
                      ),
                    ),
      floatingActionButton: FloatingActionButton(
        onPressed: _newUpload,
        child: const Icon(Icons.add),
      ),
    );
  }
}
