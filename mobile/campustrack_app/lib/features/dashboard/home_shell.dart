import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../activity/activity_screen.dart';
import '../attendance/attendance_screen.dart';
import '../grades/grades_screen.dart';
import '../notifications/notifications_screen.dart';
import '../profile/profile_screen.dart';
import 'home_screen.dart';

/// The signed-in shell.
///
/// The tab set differs by role: a parent's centre of gravity is their child's day, while a
/// student's is their own work. Rather than one menu with irrelevant entries greyed out,
/// each portal gets the five destinations that matter to it.
class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key});

  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final isGuardian = auth.profile.isGuardian;
    final unread = ref.watch(unreadCountProvider).valueOrNull ?? 0;

    final destinations = <_Destination>[
      const _Destination(
        icon: Icons.home_outlined,
        selectedIcon: Icons.home_rounded,
        label: 'Home',
        screen: HomeScreen(),
      ),
      const _Destination(
        icon: Icons.timeline_outlined,
        selectedIcon: Icons.timeline_rounded,
        label: 'Activity',
        screen: ActivityScreen(),
      ),
      const _Destination(
        icon: Icons.fact_check_outlined,
        selectedIcon: Icons.fact_check_rounded,
        label: 'Attendance',
        screen: AttendanceScreen(),
      ),
      // Grades sit behind the guardian link's academic permission, so a guardian authorised
      // only for pickup never sees the tab at all.
      if (!isGuardian || _canSeeAcademics(ref))
        const _Destination(
          icon: Icons.school_outlined,
          selectedIcon: Icons.school_rounded,
          label: 'Grades',
          screen: GradesScreen(),
        ),
      const _Destination(
        icon: Icons.notifications_outlined,
        selectedIcon: Icons.notifications_rounded,
        label: 'Alerts',
        screen: NotificationsScreen(),
      ),
      const _Destination(
        icon: Icons.person_outline_rounded,
        selectedIcon: Icons.person_rounded,
        label: 'Profile',
        screen: ProfileScreen(),
      ),
    ];

    // Guards against an out-of-range index if the tab set shrinks after a permission change.
    final safeIndex = _index.clamp(0, destinations.length - 1);

    return Scaffold(
      body: IndexedStack(
        index: safeIndex,
        // IndexedStack keeps each tab's scroll position and loaded data, so switching back
        // to Activity does not re-fetch and re-scroll the day.
        children: destinations.map((d) => d.screen).toList(),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: safeIndex,
        onDestinationSelected: (index) => setState(() => _index = index),
        destinations: [
          for (final destination in destinations)
            NavigationDestination(
              icon: destination.label == 'Alerts' && unread > 0
                  ? Badge.count(count: unread, child: Icon(destination.icon))
                  : Icon(destination.icon),
              selectedIcon: destination.label == 'Alerts' && unread > 0
                  ? Badge.count(count: unread, child: Icon(destination.selectedIcon))
                  : Icon(destination.selectedIcon),
              label: destination.label,
            ),
        ],
      ),
    );
  }

  bool _canSeeAcademics(WidgetRef ref) {
    final children = ref.watch(childrenProvider).valueOrNull;
    if (children == null || children.isEmpty) return true;

    final activeId = ref.watch(activeStudentIdProvider);
    final active = children.where((c) => c.studentId == activeId).firstOrNull ?? children.first;
    return active.canViewAcademics;
  }
}

class _Destination {
  const _Destination({
    required this.icon,
    required this.selectedIcon,
    required this.label,
    required this.screen,
  });

  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final Widget screen;
}

/// The child switcher shown at the top of a parent's screens.
///
/// Only rendered when a guardian actually has more than one child: a single-child parent
/// should never have to interact with a picker.
class ChildSwitcher extends ConsumerWidget {
  const ChildSwitcher({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final children = ref.watch(childrenProvider);

    return children.when(
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
      data: (list) {
        if (list.length < 2) return const SizedBox.shrink();

        final activeId = ref.watch(activeStudentIdProvider);

        return SizedBox(
          height: 82,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap, vertical: 8),
            itemCount: list.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (context, index) {
              final child = list[index];
              final selected = child.studentId == activeId;

              return _ChildChip(
                child: child,
                selected: selected,
                onTap: () =>
                    ref.read(selectedChildProvider.notifier).state = child.studentId,
              );
            },
          ),
        );
      },
    );
  }
}

class _ChildChip extends StatelessWidget {
  const _ChildChip({required this.child, required this.selected, required this.onTap});

  final Child child;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        decoration: BoxDecoration(
          color: selected ? theme.colorScheme.primary : theme.colorScheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? Colors.transparent : theme.dividerColor),
        ),
        child: Row(
          children: [
            Stack(
              children: [
                CircleAvatar(
                  radius: 18,
                  backgroundColor: selected
                      ? Colors.white24
                      : theme.colorScheme.primary.withValues(alpha: 0.14),
                  child: Text(
                    _initials(child.name),
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: selected ? Colors.white : theme.colorScheme.primary,
                    ),
                  ),
                ),
                // A live presence dot on the avatar answers "is she in school?" without
                // opening anything.
                if (child.isOnSite)
                  Positioned(
                    right: 0,
                    bottom: 0,
                    child: Container(
                      width: 11,
                      height: 11,
                      decoration: BoxDecoration(
                        color: AppTheme.success,
                        shape: BoxShape.circle,
                        border: Border.all(
                          color: selected ? theme.colorScheme.primary : theme.colorScheme.surface,
                          width: 2,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(width: 10),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  child.firstName,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: selected ? Colors.white : null,
                  ),
                ),
                Text(
                  child.presenceLabel,
                  style: TextStyle(
                    fontSize: 11,
                    color: selected ? Colors.white70 : AppTheme.slate500,
                  ),
                ),
              ],
            ),
            const SizedBox(width: 4),
          ],
        ),
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
