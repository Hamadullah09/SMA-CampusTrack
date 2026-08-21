import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:url_launcher/url_launcher.dart';

/// Scans the QR code a teacher printed/projected for an assignment or
/// notes file and opens the encoded download link.
class QrScanScreen extends StatefulWidget {
  const QrScanScreen({super.key});
  @override
  State<QrScanScreen> createState() => _QrScanScreenState();
}

class _QrScanScreenState extends State<QrScanScreen> {
  bool _handled = false;

  Future<void> _onDetect(BarcodeCapture capture) async {
    if (_handled) return;
    final value = capture.barcodes.firstOrNull?.rawValue;
    if (value == null) return;

    final uri = Uri.tryParse(value);
    // only accept download links, not arbitrary QR content
    if (uri == null || !uri.path.contains('/api/assignments/download/')) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('This is not a CampusTrack assignment QR code.')));
      return;
    }
    _handled = true;
    await launchUrl(uri, mode: LaunchMode.externalApplication);
    if (mounted) Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Scan assignment QR')),
      body: MobileScanner(onDetect: _onDetect),
    );
  }
}
