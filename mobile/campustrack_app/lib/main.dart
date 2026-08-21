import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/providers.dart';
import 'core/theme.dart';
import 'features/auth/login_screen.dart';
import 'features/dashboard/home_shell.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  // Firebase is initialised lazily inside PushService rather than here: a school that has
  // not configured Firebase must still get a fully working app, and a failed
  // Firebase.initializeApp() at startup would otherwise show a black screen.
  runApp(const ProviderScope(child: CampusTrackApp()));
}

class CampusTrackApp extends ConsumerWidget {
  const CampusTrackApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authProvider);

    return MaterialApp(
      title: 'SMA Campus Track',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      // Follows the phone's setting: parents check this app late in the evening.
      themeMode: ThemeMode.system,
      home: switch (auth) {
        AuthLoading() => const _SplashScreen(),
        AuthSignedOut() => const LoginScreen(),
        AuthSignedIn() => const HomeShell(),
      },
    );
  }
}

class _SplashScreen extends StatelessWidget {
  const _SplashScreen();

  @override
  Widget build(BuildContext context) => Scaffold(
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 64,
                height: 64,
                decoration: BoxDecoration(
                  gradient: const LinearGradient(
                    colors: [AppTheme.brandLight, AppTheme.brandDark],
                  ),
                  borderRadius: BorderRadius.circular(18),
                ),
                child: const Icon(Icons.sensors_rounded, color: Colors.white, size: 32),
              ),
              const SizedBox(height: 20),
              Text('SMA Campus Track', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 20),
              const SizedBox(
                width: 22,
                height: 22,
                child: CircularProgressIndicator(strokeWidth: 2.5),
              ),
            ],
          ),
        ),
      );
}
