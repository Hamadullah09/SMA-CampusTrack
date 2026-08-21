import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../../shared/widgets/common.dart';
import '../dashboard/home_shell.dart';

/// Attendance history, presented as a record a parent can check rather than a report to
/// interpret: the headline percentage first, then every day with the times behind it.
class AttendanceScreen extends ConsumerWidget {
  const AttendanceScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final attendance = ref.watch(attendanceProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Attendance')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(attendanceProvider),
        child: attendance.when(
          loading: () => const SkeletonList(),
          error: (error, _) => ListView(children: [
            const SizedBox(height: 60),
            ErrorView(
              message: error.toString(),
              onRetry: () => ref.invalidate(attendanceProvider),
            ),
          ]),
          data: (result) {
            final summary = result.summary;
            final tone = summary.attendancePercentage >= 90
                ? AppTheme.success
                : summary.attendancePercentage >= 75
                    ? AppTheme.warning
                    : AppTheme.danger;

            return ListView(
              padding: const EdgeInsets.only(bottom: 32),
              children: [
                const ChildSwitcher(),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: Card(
                    child: Padding(
                      padding: const EdgeInsets.all(AppTheme.gap + 4),
                      child: Column(
                        children: [
                          Text(
                            '${summary.attendancePercentage.toStringAsFixed(0)}%',
                            style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                                  fontSize: 44,
                                  fontWeight: FontWeight.w800,
                                  color: tone,
                                ),
                          ),
                          const SizedBox(height: 4),
                          Text(
                            'Attendance over the last 30 days',
                            style: Theme.of(context)
                                .textTheme
                                .bodySmall
                                ?.copyWith(color: AppTheme.slate500),
                          ),
                          const SizedBox(height: 18),
                          ClipRRect(
                            borderRadius: BorderRadius.circular(999),
                            child: LinearProgressIndicator(
                              value: (summary.attendancePercentage / 100).clamp(0, 1),
                              minHeight: 8,
                              backgroundColor: tone.withValues(alpha: 0.15),
                              valueColor: AlwaysStoppedAnimation(tone),
                            ),
                          ),
                          const SizedBox(height: 18),
                          Row(
                            mainAxisAlignment: MainAxisAlignment.spaceEvenly,
                            children: [
                              _Count(
                                  label: 'Present',
                                  value: summary.presentDays,
                                  tone: AppTheme.success),
                              _Count(
                                  label: 'Late',
                                  value: summary.lateDays,
                                  tone: AppTheme.warning),
                              _Count(
                                  label: 'Absent',
                                  value: summary.absentDays,
                                  tone: AppTheme.danger),
                            ],
                          ),
                          // Stated plainly rather than left for the parent to infer from the
                          // percentage: this is the fact the school will act on.
                          if (summary.isBelowRequirement) ...[
                            const SizedBox(height: 16),
                            Container(
                              padding: const EdgeInsets.all(12),
                              decoration: BoxDecoration(
                                color: AppTheme.warning.withValues(alpha: 0.12),
                                borderRadius: BorderRadius.circular(10),
                              ),
                              child: const Row(children: [
                                Icon(Icons.info_outline_rounded,
                                    size: 18, color: AppTheme.warning),
                                SizedBox(width: 10),
                                Expanded(
                                  child: Text(
                                    'This is below the attendance the school requires. '
                                    'Please contact the office if you need support.',
                                    style: TextStyle(fontSize: 13, color: AppTheme.warning),
                                  ),
                                ),
                              ]),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
                const Padding(
                  padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: SectionHeader(title: 'Day by day'),
                ),
                if (result.days.isEmpty)
                  const EmptyView(
                    icon: Icons.event_note_rounded,
                    title: 'No attendance recorded yet',
                    message: 'Days appear here once the school gate starts reading the card.',
                  )
                else
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap),
                    child: Card(
                      child: Column(children: [
                        for (var i = 0; i < result.days.length; i++) ...[
                          if (i > 0) const Divider(height: 1),
                          _DayRow(day: result.days[i]),
                        ],
                      ]),
                    ),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _Count extends StatelessWidget {
  const _Count({required this.label, required this.value, required this.tone});

  final String label;
  final int value;
  final Color tone;

  @override
  Widget build(BuildContext context) => Column(children: [
        Text(
          '$value',
          style: Theme.of(context)
              .textTheme
              .titleLarge
              ?.copyWith(color: tone, fontWeight: FontWeight.w700),
        ),
        Text(
          label,
          style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
        ),
      ]);
}

class _DayRow extends StatelessWidget {
  const _DayRow({required this.day});

  final AttendanceDay day;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final arrival = day.firstEntryAtUtc?.toLocal();
    final departure = day.lastExitAtUtc?.toLocal();

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap, vertical: 12),
      child: Row(children: [
        SizedBox(
          width: 48,
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(
              DateFormat('d MMM').format(day.date),
              style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
            ),
            Text(
              DateFormat('EEE').format(day.date),
              style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate400),
            ),
          ]),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            StatusChip(label: day.status, dense: true),
            if (arrival != null) ...[
              const SizedBox(height: 4),
              Text(
                departure == null
                    ? 'In at ${DateFormat('h:mm a').format(arrival)}'
                    : '${DateFormat('h:mm a').format(arrival)} to '
                        '${DateFormat('h:mm a').format(departure)}',
                style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
              ),
            ],
          ]),
        ),
        if (day.lateMinutes > 0)
          Text(
            '${day.lateMinutes}m late',
            style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.warning),
          ),
      ]),
    );
  }
}
