import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../../shared/widgets/common.dart';
import 'home_shell.dart';

/// The landing screen.
///
/// The first thing on it answers the question the app is opened for: is my child at school
/// right now, and when did they arrive. Everything below that is context.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authProvider);
    if (auth is! AuthSignedIn) return const SizedBox.shrink();

    final profile = auth.profile;
    final greeting = _greeting();

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(greeting, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500,
                color: AppTheme.slate500)),
            Text(profile.fullName.split(' ').first),
          ],
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref
            ..invalidate(childrenProvider)
            ..invalidate(activityProvider(null))
            ..invalidate(attendanceProvider)
            ..invalidate(timetableProvider)
            ..invalidate(unreadCountProvider);
        },
        child: ListView(
          padding: const EdgeInsets.only(bottom: 32),
          children: [
            const ChildSwitcher(),
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
              child: _PresenceCard(),
            ),
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
              child: _AttendanceStrip(),
            ),
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
              child: _TodayLessons(),
            ),
          ],
        ),
      ),
    );
  }

  static String _greeting() {
    final hour = DateTime.now().hour;
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }
}

/// Where the child is now, and when they arrived — the headline of the whole app.
class _PresenceCard extends ConsumerWidget {
  const _PresenceCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activity = ref.watch(activityProvider(null));
    final theme = Theme.of(context);

    return activity.when(
      loading: () => const Card(
        child: Padding(
          padding: EdgeInsets.all(AppTheme.gap),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Skeleton(height: 14, width: 110),
              SizedBox(height: 12),
              Skeleton(height: 24, width: 180),
            ],
          ),
        ),
      ),
      error: (error, _) => Card(
        child: Padding(
          padding: const EdgeInsets.all(AppTheme.gap),
          child: Row(
            children: [
              const Icon(Icons.cloud_off_rounded, color: AppTheme.slate400),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Could not check the latest status.',
                  style: theme.textTheme.bodyMedium?.copyWith(color: AppTheme.slate500),
                ),
              ),
              TextButton(
                onPressed: () => ref.invalidate(activityProvider(null)),
                child: const Text('Retry'),
              ),
            ],
          ),
        ),
      ),
      data: (result) {
        final presence = result.presence;
        final state = presence?['state'] as String? ?? 'Outside';
        final location = presence?['location'] as String?;
        final onSite = state != 'Outside';

        final arrival = result.timeline
            .where((e) => e.isArrival)
            .map((e) => e.occurredAtUtc.toLocal())
            .firstOrNull;
        final departure = result.timeline
            .where((e) => e.isDeparture)
            .map((e) => e.occurredAtUtc.toLocal())
            .lastOrNull;

        final tone = onSite ? AppTheme.success : AppTheme.slate500;

        return Card(
          child: Padding(
            padding: const EdgeInsets.all(AppTheme.gap + 2),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      width: 52,
                      height: 52,
                      decoration: BoxDecoration(
                        color: tone.withValues(alpha: 0.13),
                        shape: BoxShape.circle,
                      ),
                      child: Icon(
                        onSite ? Icons.school_rounded : Icons.home_rounded,
                        color: tone,
                        size: 26,
                      ),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            switch (state) {
                              'OnCampus' => 'At school',
                              'InRoom' => location == null ? 'In class' : 'In $location',
                              _ => 'Not at school',
                            },
                            style: theme.textTheme.titleLarge,
                          ),
                          const SizedBox(height: 2),
                          Text(
                            onSite
                                ? 'Checked in and on site'
                                : departure != null
                                    ? 'Left at ${DateFormat('h:mm a').format(departure)}'
                                    : 'No arrival recorded yet today',
                            style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                if (arrival != null || departure != null) ...[
                  const SizedBox(height: 18),
                  const Divider(height: 1),
                  const SizedBox(height: 14),
                  Row(
                    children: [
                      Expanded(
                        child: _TimeFact(
                          icon: Icons.login_rounded,
                          label: 'Arrived',
                          value: arrival == null ? '—' : DateFormat('h:mm a').format(arrival),
                          tone: AppTheme.success,
                        ),
                      ),
                      Container(width: 1, height: 34, color: theme.dividerColor),
                      Expanded(
                        child: _TimeFact(
                          icon: Icons.logout_rounded,
                          label: 'Left',
                          value: departure == null ? '—' : DateFormat('h:mm a').format(departure),
                          tone: AppTheme.slate500,
                        ),
                      ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        );
      },
    );
  }
}

class _TimeFact extends StatelessWidget {
  const _TimeFact({
    required this.icon,
    required this.label,
    required this.value,
    required this.tone,
  });

  final IconData icon;
  final String label;
  final String value;
  final Color tone;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 14, color: tone),
              const SizedBox(width: 6),
              Text(
                label.toUpperCase(),
                style: Theme.of(context).textTheme.labelSmall?.copyWith(color: AppTheme.slate500),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(value, style: Theme.of(context).textTheme.titleMedium),
        ],
      );
}

class _AttendanceStrip extends ConsumerWidget {
  const _AttendanceStrip();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final attendance = ref.watch(attendanceProvider);

    return attendance.when(
      loading: () => const Padding(
        padding: EdgeInsets.only(top: AppTheme.gap),
        child: Skeleton(height: 92, radius: 14),
      ),
      error: (_, __) => const SizedBox.shrink(),
      data: (result) {
        final summary = result.summary;
        final tone = summary.attendancePercentage >= 90
            ? AppTheme.success
            : summary.attendancePercentage >= 75
                ? AppTheme.warning
                : AppTheme.danger;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const SectionHeader(title: 'Attendance, last 30 days'),
            Row(
              children: [
                Expanded(
                  child: StatTile(
                    label: 'Attendance',
                    value: '${summary.attendancePercentage.toStringAsFixed(0)}%',
                    caption: '${summary.presentDays} of ${summary.totalDays} days',
                    colour: tone,
                    icon: Icons.check_circle_outline_rounded,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: StatTile(
                    label: 'Absences',
                    value: '${summary.absentDays}',
                    caption: summary.lateDays > 0 ? '${summary.lateDays} late arrivals' : 'No late arrivals',
                    colour: summary.absentDays > 0 ? AppTheme.danger : AppTheme.success,
                    icon: Icons.event_busy_rounded,
                  ),
                ),
              ],
            ),
          ],
        );
      },
    );
  }
}

class _TodayLessons extends ConsumerWidget {
  const _TodayLessons();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final timetable = ref.watch(timetableProvider);
    // ISO weekday: Flutter's DateTime.weekday is already 1 = Monday.
    final today = DateTime.now().weekday;

    return timetable.when(
      loading: () => const Padding(
        padding: EdgeInsets.only(top: AppTheme.gap),
        child: Skeleton(height: 120, radius: 14),
      ),
      error: (_, __) => const SizedBox.shrink(),
      data: (entries) {
        final lessons = entries.where((e) => e.dayOfWeek == today && !e.isBreak).toList()
          ..sort((a, b) => a.startTime.compareTo(b.startTime));

        if (lessons.isEmpty) {
          return const Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SectionHeader(title: 'Today'),
              Card(
                child: Padding(
                  padding: EdgeInsets.all(AppTheme.gap),
                  child: Row(
                    children: [
                      Icon(Icons.event_available_rounded, color: AppTheme.slate400),
                      SizedBox(width: 12),
                      Expanded(child: Text('No lessons scheduled today.')),
                    ],
                  ),
                ),
              ),
            ],
          );
        }

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SectionHeader(title: 'Today', action: Text('${lessons.length} lessons',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AppTheme.slate500))),
            Card(
              child: Column(
                children: [
                  for (var i = 0; i < lessons.length; i++) ...[
                    if (i > 0) const Divider(height: 1, indent: 76),
                    _LessonRow(entry: lessons[i]),
                  ],
                ],
              ),
            ),
          ],
        );
      },
    );
  }
}

class _LessonRow extends StatelessWidget {
  const _LessonRow({required this.entry});

  final TimetableEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colour = _parseColour(entry.subjectColour) ?? theme.colorScheme.primary;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap, vertical: 12),
      child: Row(
        children: [
          SizedBox(
            width: 52,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(entry.displayStart,
                    style: theme.textTheme.bodySmall?.copyWith(fontWeight: FontWeight.w600)),
                Text(entry.displayEnd,
                    style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate400)),
              ],
            ),
          ),
          Container(
            width: 3,
            height: 34,
            decoration: BoxDecoration(color: colour, borderRadius: BorderRadius.circular(2)),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(entry.subjectName, style: theme.textTheme.bodyLarge
                    ?.copyWith(fontWeight: FontWeight.w600)),
                if (entry.teacherName != null || entry.classroomName != null)
                  Text(
                    [entry.teacherName, entry.classroomName]
                        .where((e) => e != null && e.isNotEmpty)
                        .join(' · '),
                    style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Subject colours arrive as "#3b82f6" from the school's own configuration.
  static Color? _parseColour(String? hex) {
    if (hex == null || !hex.startsWith('#')) return null;
    final value = int.tryParse(hex.substring(1), radix: 16);
    if (value == null) return null;
    return Color(hex.length == 7 ? 0xFF000000 | value : value);
  }
}
