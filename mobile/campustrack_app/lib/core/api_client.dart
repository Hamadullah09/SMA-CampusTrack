import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Base URL of the CampusTrack API.
///
/// Override at build time so one binary can be pointed at staging or production:
///   flutter build apk --dart-define=API_BASE_URL=https://school.example.com
///
/// The default reaches the host machine from an Android emulator (10.0.2.2 is the
/// emulator's alias for the developer's localhost).
const String kApiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://10.0.2.2:5080',
);

/// A failure the UI can show a person, rather than a raw transport error.
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode, this.code});

  final String message;
  final int? statusCode;
  final String? code;

  bool get isUnauthorised => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isOffline => statusCode == null;

  @override
  String toString() => message;
}

/// Stores credentials in the platform keystore/keychain.
///
/// These tokens grant access to a child's movement history, so they are kept in
/// encrypted platform storage rather than in preferences, and cleared completely on
/// sign-out.
class TokenStorage {
  TokenStorage([FlutterSecureStorage? storage])
      : _storage = storage ??
            const FlutterSecureStorage(
              aOptions: AndroidOptions(encryptedSharedPreferences: true),
              iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock),
            );

  final FlutterSecureStorage _storage;

  static const _accessKey = 'campustrack.access';
  static const _refreshKey = 'campustrack.refresh';
  static const _profileKey = 'campustrack.profile';

  Future<String?> get accessToken => _storage.read(key: _accessKey);
  Future<String?> get refreshToken => _storage.read(key: _refreshKey);
  Future<String?> get cachedProfile => _storage.read(key: _profileKey);

  Future<void> save({
    required String accessToken,
    required String refreshToken,
    String? profileJson,
  }) async {
    await Future.wait([
      _storage.write(key: _accessKey, value: accessToken),
      _storage.write(key: _refreshKey, value: refreshToken),
      if (profileJson != null) _storage.write(key: _profileKey, value: profileJson),
    ]);
  }

  Future<void> clear() => _storage.deleteAll();
}

/// The single HTTP client for the app.
///
/// It owns two responsibilities the rest of the code should never repeat: attaching the
/// access token, and transparently refreshing it when the server says it has expired.
class ApiClient {
  ApiClient({required TokenStorage storage, Dio? dio, this.onSessionExpired})
      : _storage = storage,
        _dio = dio ??
            Dio(BaseOptions(
              baseUrl: '$kApiBaseUrl/api/v1',
              connectTimeout: const Duration(seconds: 15),
              receiveTimeout: const Duration(seconds: 30),
              headers: {'Content-Type': 'application/json'},
              // 4xx responses carry a readable problem body, so let the interceptor
              // handle them rather than throwing before it can be read.
              validateStatus: (status) => status != null && status < 500,
            )) {
    _dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _storage.accessToken;
          if (token != null) options.headers['Authorization'] = 'Bearer $token';
          handler.next(options);
        },
        onResponse: (response, handler) async {
          if (response.statusCode == 401 && !_isAuthRoute(response.requestOptions.path)) {
            final retried = await _refreshAndRetry(response.requestOptions);
            if (retried != null) return handler.resolve(retried);

            await _storage.clear();
            onSessionExpired?.call();
          }
          handler.next(response);
        },
      ),
    );
  }

  final Dio _dio;
  final TokenStorage _storage;

  /// Called when the session cannot be recovered, so the app can return to sign-in.
  final void Function()? onSessionExpired;

  /// A single in-flight refresh shared by all callers.
  ///
  /// A dashboard fires several requests at once. Because refresh tokens rotate on use,
  /// letting each 401 start its own refresh would invalidate the token family and sign
  /// the user out — the very thing rotation exists to detect.
  Future<bool>? _refreshInFlight;

  bool _isAuthRoute(String path) => path.contains('/auth/');

  Future<Response<dynamic>?> _refreshAndRetry(RequestOptions original) async {
    final refreshed = await (_refreshInFlight ??= _refreshToken().whenComplete(() {
      _refreshInFlight = null;
    }));

    if (!refreshed) return null;

    final token = await _storage.accessToken;
    original.headers['Authorization'] = 'Bearer $token';

    try {
      return await _dio.fetch(original);
    } on DioException {
      return null;
    }
  }

  Future<bool> _refreshToken() async {
    final refresh = await _storage.refreshToken;
    if (refresh == null) return false;

    try {
      // A bare Dio instance: going through _dio would re-enter the interceptor.
      final response = await Dio(BaseOptions(baseUrl: '$kApiBaseUrl/api/v1'))
          .post<Map<String, dynamic>>('/auth/refresh', data: {'refreshToken': refresh});

      final data = response.data;
      if (data == null) return false;

      await _storage.save(
        accessToken: data['accessToken'] as String,
        refreshToken: data['refreshToken'] as String,
      );
      return true;
    } catch (_) {
      return false;
    }
  }

  Future<T> get<T>(String path, {Map<String, dynamic>? query}) async =>
      _unwrap<T>(() => _dio.get<dynamic>(path, queryParameters: _clean(query)));

  Future<T> post<T>(String path, {Object? body, Map<String, dynamic>? query}) async =>
      _unwrap<T>(() => _dio.post<dynamic>(path, data: body, queryParameters: _clean(query)));

  Future<T> put<T>(String path, {Object? body}) async =>
      _unwrap<T>(() => _dio.put<dynamic>(path, data: body));

  Future<T> delete<T>(String path) async => _unwrap<T>(() => _dio.delete<dynamic>(path));

  /// Runs a request and converts every failure mode into an [ApiException] whose message
  /// is safe to show a parent.
  Future<T> _unwrap<T>(Future<Response<dynamic>> Function() send) async {
    try {
      final response = await send();
      final status = response.statusCode ?? 0;

      if (status >= 400) throw _fromResponse(response);
      return response.data as T;
    } on DioException catch (error) {
      if (error.response != null) throw _fromResponse(error.response!);

      throw switch (error.type) {
        DioExceptionType.connectionTimeout ||
        DioExceptionType.sendTimeout ||
        DioExceptionType.receiveTimeout =>
          ApiException('The school server is taking too long to respond. Please try again.'),
        DioExceptionType.connectionError =>
          ApiException('No connection. Check your internet and try again.'),
        _ => ApiException('Something went wrong. Please try again.'),
      };
    }
  }

  ApiException _fromResponse(Response<dynamic> response) {
    final status = response.statusCode;
    final body = response.data;

    // The API returns ProblemDetails; prefer its wording, which is already written for
    // a non-technical reader.
    if (body is Map<String, dynamic>) {
      final errors = body['errors'];
      if (errors is Map<String, dynamic> && errors.isNotEmpty) {
        final first = errors.values.first;
        if (first is List && first.isNotEmpty) {
          return ApiException('${first.first}', statusCode: status, code: body['code'] as String?);
        }
      }

      final detail = body['detail'] ?? body['title'] ?? body['message'];
      if (detail is String && detail.isNotEmpty) {
        return ApiException(detail, statusCode: status, code: body['code'] as String?);
      }
    }

    return ApiException(
      switch (status) {
        401 => 'Your session has ended. Please sign in again.',
        403 => 'You do not have access to this information.',
        404 => 'That information could not be found.',
        429 => 'Too many attempts. Please wait a moment.',
        _ => 'Something went wrong. Please try again.',
      },
      statusCode: status,
    );
  }

  /// Dio sends nulls as empty query values; strip them so the API sees clean parameters.
  Map<String, dynamic>? _clean(Map<String, dynamic>? query) {
    if (query == null) return null;
    final cleaned = <String, dynamic>{};
    query.forEach((key, value) {
      if (value != null) cleaned[key] = value;
    });
    return cleaned.isEmpty ? null : cleaned;
  }
}
