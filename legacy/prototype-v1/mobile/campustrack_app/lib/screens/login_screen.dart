import 'package:flutter/material.dart';
import '../core/api_client.dart';
import '../core/push_service.dart';
import '../core/session.dart';
import 'parent/parent_home.dart';
import 'student/student_home.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _username = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  Future<void> _login() async {
    setState(() { _busy = true; _error = null; });
    try {
      final data = await Api.post('/api/auth/login',
          {'username': _username.text.trim(), 'password': _password.text});
      if (data['role'] != 'Parent' && data['role'] != 'Student') {
        setState(() => _error = 'This app is for parents and students. '
            'Teachers and admins use the web portal.');
        return;
      }
      await Session.save(data);
      await PushService.registerToken();
      if (!mounted) return;
      Navigator.of(context).pushReplacement(MaterialPageRoute(
          builder: (_) => Session.role == 'Parent'
              ? const ParentHome()
              : const StudentHome()));
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.school, size: 72, color: Color(0xFF1A56A8)),
              const SizedBox(height: 8),
              Text('CampusTrack',
                  style: Theme.of(context).textTheme.headlineMedium),
              const Text('Parent & Student portal'),
              const SizedBox(height: 32),
              TextField(
                controller: _username,
                decoration: const InputDecoration(
                    labelText: 'Username', border: OutlineInputBorder()),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _password,
                obscureText: true,
                decoration: const InputDecoration(
                    labelText: 'Password', border: OutlineInputBorder()),
                onSubmitted: (_) => _login(),
              ),
              const SizedBox(height: 16),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(_error!,
                      style: const TextStyle(color: Colors.red)),
                ),
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  onPressed: _busy ? null : _login,
                  child: _busy
                      ? const SizedBox(
                          width: 18, height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('Sign in'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
