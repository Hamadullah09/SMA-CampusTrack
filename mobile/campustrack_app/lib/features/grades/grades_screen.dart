import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../../shared/widgets/common.dart';
import '../dashboard/home_shell.dart';

/// Academic results, grouped by subject first.
///
/// A flat list of every mark is how a gradebook thinks. A parent thinks in subjects and
/// wants to know which one needs attention, so the subject averages come before the detail.
class GradesScreen extends ConsumerWidget {
  const GradesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final grades = ref.watch(gradesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Grades')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(gradesProvider),
        child: grades.when(
          loading: () => const SkeletonList(),
          error: (error, _) => ListView(children: [
            const SizedBox(height: 60),
            ErrorView(message: error.toString(), onRetry: () => ref.invalidate(gradesProvider)),
          ]),
          data: (result) {
            if (result.grades.isEmpty) {
              return ListView(children: const [
                ChildSwitcher(),
                SizedBox(height: 40),
                EmptyView(
                  icon: Icons.school_rounded,
                  title: 'No results published yet',
                  message: 'Marks appear here once teachers publish them.',
                ),
              ]);
            }

            final tone = result.overall >= 75
                ? AppTheme.success
                : result.overall >= 50
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
                      child: Column(children: [
                        Text(
                          '${result.overall.toStringAsFixed(1)}%',
                          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                                fontSize: 40,
                                fontWeight: FontWeight.w800,
                                color: tone,
                              ),
                        ),
                        Text(
                          'Overall average across ${result.grades.length} assessments',
                          textAlign: TextAlign.center,
                          style: Theme.of(context)
                              .textTheme
                              .bodySmall
                              ?.copyWith(color: AppTheme.slate500),
                        ),
                      ]),
                    ),
                  ),
                ),
                const Padding(
                  padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: SectionHeader(title: 'By subject'),
                ),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: Card(
                    child: Column(children: [
                      for (var i = 0; i < result.bySubject.length; i++) ...[
                        if (i > 0) const Divider(height: 1),
                        _SubjectRow(subject: result.bySubject[i]),
                      ],
                    ]),
                  ),
                ),
                const Padding(
                  padding: EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: SectionHeader(title: 'Recent results'),
                ),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap),
                  child: Card(
                    child: Column(children: [
                      for (var i = 0; i < result.grades.length && i < 20; i++) ...[
                        if (i > 0) const Divider(height: 1),
                        _GradeRow(grade: result.grades[i]),
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

class _SubjectRow extends StatelessWidget {
  const _SubjectRow({required this.subject});

  final SubjectAverage subject;

  @override
  Widget build(BuildContext context) {
    final tone = subject.average >= 75
        ? AppTheme.success
        : subject.average >= 50
            ? AppTheme.warning
            : AppTheme.danger;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap, vertical: 12),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(
              subject.subject,
              style: Theme.of(context)
                  .textTheme
                  .bodyLarge
                  ?.copyWith(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 6),
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(
                value: (subject.average / 100).clamp(0, 1),
                minHeight: 6,
                backgroundColor: tone.withValues(alpha: 0.14),
                valueColor: AlwaysStoppedAnimation(tone),
              ),
            ),
          ]),
        ),
        const SizedBox(width: 14),
        Text(
          '${subject.average.toStringAsFixed(0)}%',
          style: Theme.of(context).textTheme.titleMedium?.copyWith(color: tone),
        ),
      ]),
    );
  }
}

class _GradeRow extends StatelessWidget {
  const _GradeRow({required this.grade});

  final GradeEntry grade;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tone = grade.percentage >= 75
        ? AppTheme.success
        : grade.percentage >= 50
            ? AppTheme.warning
            : AppTheme.danger;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppTheme.gap, vertical: 12),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(
              grade.title,
              style: theme.textTheme.bodyLarge?.copyWith(fontWeight: FontWeight.w600),
            ),
            Text(
              '${grade.subjectName} · ${DateFormat('d MMM').format(grade.recordedOn)}',
              style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
            ),
          ]),
        ),
        Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
          Text(
            '${grade.score.toStringAsFixed(0)}/${grade.maxScore.toStringAsFixed(0)}',
            style: theme.textTheme.titleMedium?.copyWith(color: tone),
          ),
          if (grade.letter != null)
            Text(
              grade.letter!,
              style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate500),
            ),
        ]),
      ]),
    );
  }
}
