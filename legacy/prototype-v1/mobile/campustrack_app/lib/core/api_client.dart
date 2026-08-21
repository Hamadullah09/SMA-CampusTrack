import 'dart:convert';
import 'package:http/http.dart' as http;
import 'session.dart';

/// Change this to your server address before building.
/// Android emulator -> host machine is 10.0.2.2.
const String kApiBaseUrl = 'http://10.0.2.2:5000';

class ApiException implements Exception {
  final String message;
  ApiException(this.message);
  @override
  String toString() => message;
}

class Api {
  static Map<String, String> _headers({bool json = true}) => {
        if (json) 'Content-Type': 'application/json',
        if (Session.token != null) 'Authorization': 'Bearer ${Session.token}',
      };

  static Future<dynamic> get(String path) async {
    final r = await http.get(Uri.parse('$kApiBaseUrl$path'), headers: _headers());
    return _handle(r);
  }

  static Future<dynamic> post(String path, [Object? body]) async {
    final r = await http.post(Uri.parse('$kApiBaseUrl$path'),
        headers: _headers(), body: body == null ? null : jsonEncode(body));
    return _handle(r);
  }

  /// multipart upload (student projects, etc.)
  static Future<dynamic> upload(String path, Map<String, String> fields,
      String fileField, String filePath) async {
    final req = http.MultipartRequest('POST', Uri.parse('$kApiBaseUrl$path'));
    req.headers.addAll(_headers(json: false));
    req.fields.addAll(fields);
    req.files.add(await http.MultipartFile.fromPath(fileField, filePath));
    final r = await http.Response.fromStream(await req.send());
    return _handle(r);
  }

  static dynamic _handle(http.Response r) {
    if (r.statusCode == 401) {
      Session.clear();
      throw ApiException('Session expired – please sign in again');
    }
    if (r.statusCode >= 400) {
      try {
        final body = jsonDecode(r.body);
        throw ApiException(body['message'] ?? 'Request failed (${r.statusCode})');
      } on ApiException {
        rethrow;
      } catch (_) {
        throw ApiException('Request failed (${r.statusCode})');
      }
    }
    if (r.body.isEmpty) return null;
    try {
      return jsonDecode(r.body);
    } catch (_) {
      return null;
    }
  }
}
