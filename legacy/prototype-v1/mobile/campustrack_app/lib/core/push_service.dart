import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'api_client.dart';

/// Firebase Cloud Messaging setup. Safe to call even before Firebase is
/// configured for the project (it just logs and continues) so the app
/// runs out of the box; run `flutterfire configure` to enable real push.
class PushService {
  static Future<void> init() async {
    try {
      await Firebase.initializeApp();
      await FirebaseMessaging.instance.requestPermission();
    } catch (e) {
      // Firebase not configured yet – in-app notification list still works.
      // ignore: avoid_print
      print('Push disabled (Firebase not configured): $e');
    }
  }

  /// After login: register this device's token with the backend so the
  /// server can push gate entry/exit and summary notifications here.
  static Future<void> registerToken() async {
    try {
      final token = await FirebaseMessaging.instance.getToken();
      if (token != null) {
        await Api.post('/api/auth/fcm-token', {'token': token});
      }
    } catch (_) {/* Firebase not configured */}
  }
}
