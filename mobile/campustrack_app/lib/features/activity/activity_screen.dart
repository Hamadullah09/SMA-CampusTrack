import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../../shared/widgets/common.dart';

/// The child's day, minute by minute.
///
/// This is the screen the whole product exists to produce: a parent opens the app and sees
/// that their child arrived at 07:48, went into the science room at 08:02, and left the
/// building at 14:15. Everything else is supporting detail.
class ActivityScreen extends ConsumerStatefulWidget {
  const ActivityScreen({super.key});

  @override
  ConsumerState<ActivityScreen> createState() => _ActivityScreenState();
}

class _ActivityScreenState extends ConsumerState<ActivityScreen> {
  DateTime _date = DateTime.now();

  bool get _isToday {
    final now = DateTime.now();
    return _date.year == now.year && _date.month == now.month && _date.day == now.day;
  }

  @override
  Widget build(BuildContext context) {
    final activity = ref.watch(activityProvider(_isToday ? null : _date));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Activity'),
        actions: [
          IconButton(
            icon: const Icon(Icons.calendar_today_rounded),
            tooltip: 'Choose a day',
            onPressed: _pickDate,
          ),
        ],
      ),
      body: Column(
        children: [
          _DateStrip(
            date: _date,
            onChanged: (date) => setState(() => _date = date),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async =>
                  ref.invalidate(activityProvider(_isToday ? null : _date)),
              child: activity.when(
                loading: () => const SkeletonList(),
                error: (error, _) => ListView(
                  children: [
                    const SizedBox(height: 60),
                    ErrorView(
                      message: error.toString(),
                      onRetry: () => ref.invalidate(activityProvider(_isToday ? null : _date)),
                    ),
                  ],
                ),
                data: (result) => _Timeline(
                  entries: result.timeline,
                  presence: result.presence,
                  isToday: _isToday,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      // A school year is the useful range; there is no data before the system was installed.
      firstDate: DateTime.now().subtract(const Duration(days: 365)),
      lastDate: DateTime.now(),
    );

    if (picked != null && mounted) setState(() => _date = picked);
  }
}

/// A week of days to tap between, because "yesterday" and "Friday" are the two questions
/// parents actually ask after "today".
class _DateStrip extends StatelessWidget {
  const _DateStrip({required this.date, required this.onChanged});

  final DateTime date;
  final ValueChanged<DateTime> onChanged;

  @override
  Widget build(BuildContext context) {
    final today = DateTime.now();
    final days = List.generate(7, (i) => today.subtract(Duration(days: 6 - i)));

    return SizedBox(
      height: 76,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        itemCount: days.length,
        separatorBuilder: (_, __) => const SizedBox(width: 8),
        itemBuilder: (context, index) {
          final day = days[index];
          final selected = day.year == date.year && day.month == date.month && day.day == date.day;

          return GestureDetector(
            onTap: () => onChanged(day),
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              width: 54,
              decoration: BoxDecoration(
                color: selected
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(context).colorScheme.surface,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(
                  color: selected ? Colors.transparent : Theme.of(context).dividerColor,
                ),
              ),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text(
                    DateFormat('EEE').format(day),
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.w600,
                      color: selected ? Colors.white70 : AppTheme.slate500,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    '${day.day}',
                    style: TextStyle(
                      fontSize: 17,
                      fontWeight: FontWeight.w700,
                      color: selected ? Colors.white : null,
                    ),
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}

class _Timeline extends StatelessWidget {
  const _Timeline({required this.entries, required this.presence, required this.isToday});

  final List<ActivityEntry> entries;
  final Map<String, dynamic>? presence;
  final bool isToday;

  @override
  Widget build(BuildContext context) {
    if (entries.isEmpty) {
      return ListView(
        children: [
          const SizedBox(height: 40),
          if (isToday && presence != null) _PresenceBanner(presence: presence!),
          EmptyView(
            icon: Icons.timeline_rounded,
            title: isToday ? 'Nothing recorded yet today' : 'No activity on this day',
            message: isToday
                ? 'Movement appears here as soon as the school gate reads the card.'
                : 'There were no card readings on this day. It may have been a holiday.',
          ),
        ],
      );
    }

    return ListView.builder(
      padding: const EdgeInsets.fromLTRB(AppTheme.gap, 8, AppTheme.gap, 32),
      itemCount: entries.length + (isToday && presence != null ? 1 : 0),
      itemBuilder: (context, index) {
        if (isToday && presence != null && index == 0) {
          return _PresenceBanner(presence: presence!);
        }

        final entryIndex = isToday && presence != null ? index - 1 : index;
        final entry = entries[entryIndex];

        return _TimelineTile(
          entry: entry,
          isFirst: entryIndex == 0,
          isLast: entryIndex == entries.length - 1,
        );
      },
    );
  }
}

/// Where the child is right now, stated plainly at the top of today's timeline.
class _PresenceBanner extends StatelessWidget {
  const _PresenceBanner({required this.presence});

  final Map<String, dynamic> presence;

  @override
  Widget build(BuildContext context) {
    final state = presence['state'] as String? ?? 'Outside';
    final location = presence['location'] as String?;
    final onSite = state != 'Outside';

    final label = switch (state) {
      'OnCampus' => 'At school',
      'InRoom' => location == null ? 'In class' : 'In $location',
      _ => 'Not at school',
    };

    final tone = onSite ? AppTheme.success : AppTheme.slate500;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(AppTheme.gap),
        child: Row(
          children: [
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                color: tone.withValues(alpha: 0.14),
                shape: BoxShape.circle,
              ),
              child: Icon(
                onSite ? Icons.location_on_rounded : Icons.home_rounded,
                color: tone,
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Right now', style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: AppTheme.slate500)),
                  const SizedBox(height: 2),
                  Text(label, style: Theme.of(context).textTheme.titleMedium),
                ],
              ),
            ),
            if (onSite)
              Container(
                width: 10,
                height: 10,
                decoration: const BoxDecoration(color: AppTheme.success, shape: BoxShape.circle),
              ),
          ],
        ),
      ),
    );
  }
}

class _TimelineTile extends StatelessWidget {
  const _TimelineTile({required this.entry, required this.isFirst, required this.isLast});

  final ActivityEntry entry;
  final bool isFirst;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final local = entry.occurredAtUtc.toLocal();
    final (icon, tone) = _visualFor(entry);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Time gutter, so the eye can scan down the day without reading each row.
          SizedBox(
            width: 62,
            child: Padding(
              padding: const EdgeInsets.only(top: 14),
              child: Text(
                DateFormat('h:mm a').format(local),
                style: theme.textTheme.bodySmall?.copyWith(
                  fontWeight: FontWeight.w600,
                  color: AppTheme.slate500,
                ),
              ),
            ),
          ),

          // The connecting rail.
          Column(
            children: [
              Container(width: 2, height: 14, color: isFirst ? Colors.transparent : theme.dividerColor),
              Container(
                width: 30,
                height: 30,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.15),
                  shape: BoxShape.circle,
                ),
                child: Icon(icon, size: 16, color: tone),
              ),
              Expanded(
                child: Container(
                  width: 2,
                  color: isLast ? Colors.transparent : theme.dividerColor,
                ),
              ),
            ],
          ),

          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(left: 12, top: 10, bottom: 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(entry.title, style: theme.textTheme.bodyLarge
                      ?.copyWith(fontWeight: FontWeight.w600)),
                  if (entry.detail != null && entry.detail!.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    Text(entry.detail!, style: theme.textTheme.bodySmall
                        ?.copyWith(color: AppTheme.slate500)),
                  ],
                  if (entry.durationMinutes != null && entry.durationMinutes! > 0) ...[
                    const SizedBox(height: 6),
                    StatusChip(
                      label: '${entry.durationMinutes} min',
                      colour: AppTheme.info,
                      dense: true,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  (IconData, Color) _visualFor(ActivityEntry entry) => switch (entry.eventType) {
        'SchoolEntry' => (Icons.login_rounded, AppTheme.success),
        'SchoolExit' => (Icons.logout_rounded, AppTheme.slate500),
        'ClassroomEntry' || 'ZoneEntry' => (Icons.meeting_room_rounded, AppTheme.info),
        'ClassroomExit' || 'ZoneExit' => (Icons.door_back_door_rounded, AppTheme.slate400),
        _ => (Icons.place_rounded, AppTheme.brand),
      };
}
