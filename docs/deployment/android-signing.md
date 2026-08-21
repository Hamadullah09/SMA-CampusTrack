# Android build and signing

The Android project lives at `mobile/campustrack_app/android/` and **is tracked in git**. It
carries the application id, the permissions, the network policy and the signing wiring. An
earlier version of the release workflow regenerated it on every run, which silently reset all
four; generating it once and committing it is the only way those decisions survive.

| Setting | Value | Why |
|---|---|---|
| Application id | `net.smatechno.campustrack` | Reverse DNS of sma-techno.net. The hyphen is dropped because a Java package segment cannot contain one. **Permanent** — Android identifies the app by this string and it can never change. |
| Namespace | `net.smatechno.campustrack` | Matches the Kotlin package and the `applicationId`. |
| `minSdk` | 23 (Android 6.0) | `flutter_secure_storage` needs 21; `local_auth`'s biometric prompt needs 23. |
| `targetSdk` | 35 (Android 15) | |
| Activity | `FlutterFragmentActivity` | `local_auth` shows a fragment-based system prompt. With the plain `FlutterActivity` it builds fine and throws the first time someone uses a fingerprint. |

## Generating the keystore

Do this once, on a machine you control. Two routes; both produce the same artefact, a PKCS#12
keystore holding one RSA key and a self-signed certificate.

**Without a JDK** (nothing to install). The script prompts for the password, so it never
appears on a command line or in shell history:

```bash
python tools/generate-release-key.py
```

**With a JDK**, if you have one or would rather use the standard tool:

```bash
keytool -genkeypair -v \
  -keystore sma-campus-track.jks \
  -storetype PKCS12 \
  -keyalg RSA -keysize 2048 -validity 10000 \
  -alias sma-campus-track \
  -dname "CN=SMA Campus Track, O=SMA Technology, C=PK"
```

`keytool` ships only with a JDK; if it is not recognised, that is why. Install one with
`winget install EclipseAdoptium.Temurin.17.JDK` **from an elevated PowerShell** (it needs a
UAC prompt), then reopen the terminal so `keytool` is on `PATH`. Or just use the Python
script and skip the install.

Run whichever you choose from a sensible working directory. A PowerShell opened as
administrator starts in `C:\Windows\System32`, which is not where this file should land.

> **Back the keystore up somewhere durable and private, together with the password.** Losing
> either means the app can never be updated again, only replaced under a new application id
> with every family uninstalling and reinstalling. `-validity 10000` is about 27 years; a
> shorter key would strand you the same way when it expired.

Nothing about the keystore belongs in this repository. `.gitignore` already refuses
`key.properties`, `*.jks`, `*.keystore` and `keys/`, which you can confirm with:

```bash
git check-ignore -v mobile/campustrack_app/android/key.properties
```

## Building locally

Copy `android/key.properties.template` to `android/key.properties` and fill in the four
values. Then:

```bash
flutter build apk --release --dart-define=API_BASE_URL=http://campus.sma-techno.net
```

Without `key.properties` the build still succeeds, signed with the **debug** key, and prints a
warning saying so. That is fine for a quick install on your own phone and must never reach
families — see the warning below.

## Building in CI

**Actions → Build Android APK → Run workflow.** Add these first, under
Settings → Secrets and variables → Actions:

| Secret | How to produce it |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | `python tools/generate-release-key.py --base64` and paste the output. (Equivalently `base64 -w0 sma-campus-track.jks`.) A secret holds text, not binary, hence the encoding. |
| `ANDROID_KEYSTORE_PASSWORD` | The store password you chose above |
| `ANDROID_KEY_ALIAS` | `sma-campus-track` |
| `ANDROID_KEY_PASSWORD` | The key password (often the same as the store password) |

Optionally add a **variable** (not a secret) named `API_BASE_URL` if the app should point
somewhere other than `http://campus.sma-techno.net`.

The workflow refuses to publish a debug-signed APK: it runs `apksigner verify --print-certs`
and fails the build if the certificate is `CN=Android Debug`. Without that check a debug
build is indistinguishable from a real one until the first update fails.

Download the artefact, then publish it from **Mobile app → Publish a build** in the admin
portal. Families download it from `campus.sma-techno.net/mobile-app`.

## Why a debug-signed build must not be distributed

Android identifies an app by its signing certificate, not its version. An app signed with the
debug key and one signed with your release key are two different apps to the system, so the
properly signed build **cannot install over** the debug one. Every family would have to
uninstall first, losing their session, and anyone who missed the message would keep running a
build that can never be updated again.

## Two things still outstanding

**1. The site is served over plain HTTP.** Android has blocked cleartext by default since
Android 9, so `android/app/src/main/res/xml/network_security_config.xml` carries an exception
for `campus.sma-techno.net`. Without it, every request from the app fails with an error that
looks like the server being down. The exception is scoped to that one host, so nothing else
is weakened — but the app carries tokens that grant access to a child's location history, and
over HTTP anyone on the same wifi can read them. MyASP.NET issues free Let's Encrypt
certificates. Once the site is on HTTPS, delete that domain from the config.

**2. Push notifications are inactive.** `firebase_messaging` needs
`android/app/google-services.json` from the Firebase console. The Gradle build applies the
Google Services plugin only when that file exists, so its absence does not break the build —
notifications are stored and shown in-app, they just are not pushed to the phone. Drop the
file in (it is gitignored) and push starts working with no code change.
