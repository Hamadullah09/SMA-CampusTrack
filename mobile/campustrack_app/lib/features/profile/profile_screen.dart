import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';

/// Account details, card status and notification settings.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final profile = auth.profile;
    final rfid = ref.watch(rfidStatusProvider);
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Profile')),
      body: ListView(
        padding: const EdgeInsets.all(AppTheme.gap),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(AppTheme.gap + 4),
              child: Row(children: [
                CircleAvatar(
                  radius: 30,
                  backgroundColor: theme.colorScheme.primary.withValues(alpha: 0.14),
                  child: Text(
                    _initials(profile.fullName),
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w700,
                      color: theme.colorScheme.primary,
                    ),
                  ),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(profile.fullName, style: theme.textTheme.titleLarge),
                    Text(
                      profile.roles.join(', '),
                      style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
                    ),
                    if (profile.schoolName != null)
                      Text(
                        profile.schoolName!,
                        style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
                      ),
                  ]),
                ),
              ]),
            ),
          ),
          const SizedBox(height: 16),

          // Card status answers the most common question a school office receives:
          // "the gate did not register my child".
          rfid.when(
            loading: () => const SizedBox.shrink(),
            error: (_, __) => const SizedBox.shrink(),
            data: (status) {
              final hasCard = status['hasActiveCard'] as bool? ?? false;
              final card = status['card'] as Map<String, dynamic>?;
              final lastSeen = card?['lastSeenLocation'] as String?;

              return Card(
                child: ListTile(
                  leading: Icon(
                    hasCard ? Icons.badge_rounded : Icons.badge_outlined,
                    color: hasCard ? AppTheme.success : AppTheme.warning,
                  ),
                  title: Text(hasCard ? 'RFID card active' : 'No card assigned'),
                  subtitle: Text(
                    hasCard
                        ? 'Card ${card?['maskedEpc'] ?? ''}'
                            '${lastSeen == null ? '' : ' · last seen at $lastSeen'}'
                        : 'Contact the school office to be issued a card.',
                  ),
                ),
              );
            },
          ),

          const SizedBox(height: 16),
          Card(
            child: Column(children: [
              ListTile(
                leading: const Icon(Icons.notifications_outlined),
                title: const Text('Notification settings'),
                subtitle: const Text('Choose what you are told about'),
                trailing: const Icon(Icons.chevron_right_rounded),
                onTap: () => Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const NotificationSettingsScreen()),
                ),
              ),
            ]),
          ),

          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: () async {
              final confirmed = await showDialog<bool>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const Text('Sign out?'),
                  content: const Text('You will need your password to sign in again.'),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(context, false),
                      child: const Text('Cancel'),
                    ),
                    FilledButton(
                      onPressed: () => Navigator.pop(context, true),
                      child: const Text('Sign out'),
                    ),
                  ],
                ),
              );

              if (confirmed ?? false) await ref.read(authProvider.notifier).signOut();
            },
            icon: const Icon(Icons.logout_rounded),
            label: const Text('Sign out'),
            style: OutlinedButton.styleFrom(foregroundColor: AppTheme.danger),
          ),
        ],
      ),
    );
  }

  static String _initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+'));
    if (parts.isEmpty) return '?';
    final first = parts.first.isEmpty ? '' : parts.first[0];
    final last = parts.length > 1 ? parts.last[0] : '';
    return '$first$last'.toUpperCase();
  }
}

/// Per-category notification switches, mirroring the preferences the API stores.
class NotificationSettingsScreen extends ConsumerStatefulWidget {
  const NotificationSettingsScreen({super.key});

  @override
  ConsumerState<NotificationSettingsScreen> createState() => _NotificationSettingsState();
}

class _NotificationSettingsState extends ConsumerState<NotificationSettingsScreen> {
  List<Map<String, dynamic>> _preferences = [];
  bool _loading = true;
  String? _error;

  /// Only the categories a parent or student can act on. The operational ones (device
  /// errors, system events) would be noise in a family's settings screen.
  static const _visible = <String, String>{
    'SchoolEntry': 'Arrival at school',
    'SchoolExit': 'Departure from school',
    'ClassroomEntry': 'Classroom movement',
    'Absence': 'Absence',
    'LateArrival': 'Late arrival',
    'Assignment': 'New assignments',
    'Quiz': 'New quizzes',
    'Grade': 'Results published',
    'DailyReport': 'Daily summary',
    'Announcement': 'School announcements',
    'Emergency': 'Emergency messages',
  };

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final data = await ref.read(repositoryProvider).notificationPreferences();
      if (!mounted) return;
      setState(() {
        _preferences = data.cast<Map<String, dynamic>>();
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.toString();
        _loading = false;
      });
    }
  }

  Future<void> _save() async {
    try {
      await ref.read(repositoryProvider).saveNotificationPreferences(_preferences);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Notification settings saved')),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not save: $error')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [TextButton(onPressed: _save, child: const Text('Save'))],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(child: Padding(padding: const EdgeInsets.all(24), child: Text(_error!)))
              : ListView(
                  children: [
                    for (final preference in _preferences)
                      if (_visible.containsKey(preference['categoryName']))
                        SwitchListTile(
                          title: Text(_visible[preference['categoryName']]!),
                          subtitle: Text(
                            preference['pushEnabled'] as bool
                                ? 'Push notification on'
                                : 'Only shown inside the app',
                          ),
                          value: preference['pushEnabled'] as bool,
                          onChanged: (value) =>
                              setState(() => preference['pushEnabled'] = value),
                        ),
                  ],
                ),
    );
  }
}
