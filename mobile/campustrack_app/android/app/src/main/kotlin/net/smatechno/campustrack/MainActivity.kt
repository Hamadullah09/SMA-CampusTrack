package net.smatechno.campustrack

import io.flutter.embedding.android.FlutterFragmentActivity

/**
 * FlutterFragmentActivity rather than FlutterActivity.
 *
 * local_auth presents the system biometric prompt, which is a fragment and needs a
 * FragmentActivity to host it. With the plain FlutterActivity the app builds and launches
 * normally and then throws the first time a parent tries to unlock with a fingerprint, so
 * the mistake surfaces on a real device rather than in CI.
 */
class MainActivity : FlutterFragmentActivity()
