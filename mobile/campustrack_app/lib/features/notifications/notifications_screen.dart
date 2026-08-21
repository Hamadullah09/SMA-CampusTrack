import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/providers.dart';
import '../../core/theme.dart';
import '../../data/models.dart';
import '../../shared/widgets/common.dart';

/// The notification inbox.
///
/// Every push the school sends is also stored here, so a parent whose phone was off, or who
/// dismissed the banner while driving, can still find out their child arrived late.
class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifications = ref.watch(notificationsProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Alerts'),
        actions: [
          TextButton(
            onPressed: () async {
              await ref.read(repositoryProvider).markAllRead();
              ref
                ..invalidate(notificationsProvider)
                ..invalidate(unreadCountProvider);
            },
            child: const Text('Mark all read'),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref
            ..invalidate(notificationsProvider)
            ..invalidate(unreadCountProvider);
        },
        child: notifications.when(
          loading: () => const SkeletonList(),
          error: (error, _) => ListView(children: [
            const SizedBox(height: 60),
            ErrorView(
              message: error.toString(),
              onRetry: () => ref.invalidate(notificationsProvider),
            ),
          ]),
          data: (items) {
            if (items.isEmpty) {
              return ListView(children: const [
                SizedBox(height: 60),
                EmptyView(
                  icon: Icons.notifications_none_rounded,
                  title: 'Nothing yet',
                  message: 'Arrivals, absences, results and school messages appear here.',
                ),
              ]);
            }

            return ListView.separated(
              padding: const EdgeInsets.all(AppTheme.gap),
              itemCount: items.length,
              separatorBuilder: (_, __) => const SizedBox(height: 10),
              itemBuilder: (context, index) =>
                  _NotificationCard(notification: items[index], onRead: () async {
                await ref.read(repositoryProvider).markRead(items[index].id);
                ref
                  ..invalidate(notificationsProvider)
                  ..invalidate(unreadCountProvider);
              }),
            );
          },
        ),
      ),
    );
  }
}

class _NotificationCard extends StatelessWidget {
  const _NotificationCard({required this.notification, required this.onRead});

  final AppNotification notification;
  final Future<void> Function() onRead;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final (icon, tone) = _visualFor(notification.category);

    return InkWell(
      borderRadius: BorderRadius.circular(AppTheme.radius),
      onTap: notification.isRead ? null : () => onRead(),
      child: Card(
        // Unread is carried by a tinted surface as well as the dot, so the distinction
        // survives greyscale and colour-blind viewing.
        color: notification.isRead ? null : theme.colorScheme.primary.withValues(alpha: 0.05),
        child: Padding(
          padding: const EdgeInsets.all(AppTheme.gap),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Container(
              width: 38,
              height: 38,
              decoration: BoxDecoration(
                color: tone.withValues(alpha: 0.13),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Icon(icon, size: 19, color: tone),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  Expanded(
                    child: Text(
                      notification.title,
                      style: theme.textTheme.bodyLarge?.copyWith(
                        fontWeight:
                            notification.isRead ? FontWeight.w500 : FontWeight.w700,
                      ),
                    ),
                  ),
                  if (!notification.isRead)
                    Container(
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(color: tone, shape: BoxShape.circle),
                    ),
                ]),
                const SizedBox(height: 4),
                Text(
                  notification.body,
                  style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate600),
                ),
                const SizedBox(height: 8),
                Text(
                  _relative(notification.createdAtUtc.toLocal()),
                  style: theme.textTheme.bodySmall?.copyWith(color: AppTheme.slate400),
                ),
              ]),
            ),
          ]),
        ),
      ),
    );
  }

  (IconData, Color) _visualFor(String category) => switch (category) {
        'SchoolEntry' => (Icons.login_rounded, AppTheme.success),
        'SchoolExit' => (Icons.logout_rounded, AppTheme.slate500),
        'ClassroomEntry' || 'ClassroomExit' => (Icons.meeting_room_rounded, AppTheme.info),
        'Absence' => (Icons.event_busy_rounded, AppTheme.danger),
        'LateArrival' => (Icons.schedule_rounded, AppTheme.warning),
        'Grade' => (Icons.grade_rounded, AppTheme.brand),
        'Assignment' => (Icons.assignment_rounded, AppTheme.info),
        'Quiz' => (Icons.quiz_rounded, AppTheme.info),
        'DailyReport' => (Icons.summarize_rounded, AppTheme.brand),
        'Emergency' => (Icons.warning_amber_rounded, AppTheme.danger),
        'Announcement' => (Icons.campaign_rounded, AppTheme.brand),
        _ => (Icons.notifications_rounded, AppTheme.slate500),
      };

  /// Relative time reads faster than a timestamp for anything recent, which is most of
  /// what lands in this inbox.
  static String _relative(DateTime when) {
    final diff = DateTime.now().difference(when);
    if (diff.inMinutes < 1) return 'Just now';
    if (diff.inMinutes < 60) return '${diff.inMinutes} min ago';
    if (diff.inHours < 24) return '${diff.inHours}h ago';
    if (diff.inDays == 1) return 'Yesterday';
    if (diff.inDays < 7) return '${diff.inDays} days ago';
    return DateFormat('d MMM').format(when);
  }
}
