import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/api_client.dart';

/// Parents give feedback in the fixed categories provided by the school
/// and see the school's replies.
class FeedbackTab extends StatefulWidget {
  final List<dynamic> children;
  const FeedbackTab({super.key, required this.children});

  @override
  State<FeedbackTab> createState() => _FeedbackTabState();
}

class _FeedbackTabState extends State<FeedbackTab> {
  List<String> _categories = [];
  List<dynamic>? _history;
  final _message = TextEditingController();
  String? _category;
  int? _studentId;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _studentId = widget.children.isNotEmpty ? widget.children.first['id'] : null;
    _load();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        Api.get('/api/feedback/categories'),
        Api.get('/api/feedback/mine'),
      ]);
      setState(() {
        _categories = List<String>.from(results[0]);
        _category ??= _categories.isNotEmpty ? _categories.first : null;
        _history = results[1];
      });
    } catch (_) {}
  }

  Future<void> _submit() async {
    if (_message.text.trim().isEmpty || _category == null || _studentId == null) return;
    setState(() => _sending = true);
    try {
      await Api.post('/api/feedback', {
        'studentId': _studentId,
        'category': _category,
        'message': _message.text.trim(),
      });
      _message.clear();
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Feedback submitted. Thank you!')));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.toString())));
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final fmt = DateFormat('dd MMM yyyy');
    return ListView(
      padding: const EdgeInsets.all(12),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('Send feedback',
                    style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 8),
                if (widget.children.length > 1)
                  DropdownButtonFormField<int>(
                    value: _studentId,
                    decoration: const InputDecoration(labelText: 'Regarding'),
                    items: widget.children
                        .map<DropdownMenuItem<int>>((c) => DropdownMenuItem(
                            value: c['id'], child: Text(c['name'])))
                        .toList(),
                    onChanged: (v) => setState(() => _studentId = v),
                  ),
                DropdownButtonFormField<String>(
                  value: _category,
                  decoration: const InputDecoration(labelText: 'Category'),
                  items: _categories
                      .map((c) => DropdownMenuItem(value: c, child: Text(c)))
                      .toList(),
                  onChanged: (v) => setState(() => _category = v),
                ),
                const SizedBox(height: 8),
                TextField(
                  controller: _message,
                  maxLines: 4,
                  decoration: const InputDecoration(
                      labelText: 'Your message', border: OutlineInputBorder()),
                ),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: _sending ? null : _submit,
                  icon: const Icon(Icons.send),
                  label: const Text('Submit'),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16),
        Text('Previous feedback',
            style: Theme.of(context).textTheme.titleMedium),
        if (_history == null)
          const Padding(
              padding: EdgeInsets.all(24),
              child: Center(child: CircularProgressIndicator()))
        else if (_history!.isEmpty)
          const Padding(
              padding: EdgeInsets.all(24),
              child: Center(child: Text('Nothing sent yet.')))
        else
          ..._history!.map((f) => Card(
                child: ListTile(
                  title: Text('[${f['category']}] ${f['message']}'),
                  subtitle: Text([
                    '${f['student']} • ${fmt.format(DateTime.parse(f['createdAt']).toLocal())} • ${f['status']}',
                    if (f['reply'] != null) 'School reply: ${f['reply']}',
                  ].join('\n')),
                  isThreeLine: f['reply'] != null,
                ),
              )),
      ],
    );
  }
}
