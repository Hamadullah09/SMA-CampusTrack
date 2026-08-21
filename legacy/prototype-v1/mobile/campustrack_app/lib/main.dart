import 'package:flutter/material.dart';
import 'core/push_service.dart';
import 'core/session.dart';
import 'screens/login_screen.dart';
import 'screens/parent/parent_home.dart';
import 'screens/student/student_home.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Session.load();
  await PushService.init();
  runApp(const CampusTrackApp());
}

class CampusTrackApp extends StatelessWidget {
  const CampusTrackApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'CampusTrack',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF1A56A8)),
        useMaterial3: true,
      ),
      home: _startScreen(),
    );
  }

  Widget _startScreen() {
    if (!Session.isLoggedIn) return const LoginScreen();
    return Session.role == 'Parent' ? const ParentHome() : const StudentHome();
  }
}
