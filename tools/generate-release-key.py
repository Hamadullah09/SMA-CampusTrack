#!/usr/bin/env python3
"""
Generate the Android release signing key without a JDK.

`keytool` is the usual way to do this, but it ships only with a JDK. This produces the same
artefact -- a PKCS#12 keystore holding one RSA key and a self-signed certificate -- which is
exactly what keytool itself creates by default (PKCS#12 has been its default store type since
JDK 9). Gradle, apksigner and Play all accept it.

The password is read from a prompt and never appears in an argument, a log or this file. It
is not echoed, and it is not written anywhere except into the encrypted keystore.

Usage:
    python tools/generate-release-key.py

Then follow the instructions it prints.
"""

from __future__ import annotations

import datetime as dt
import getpass
import os
import subprocess
import sys
from pathlib import Path

try:
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.hazmat.primitives.serialization import pkcs12
    from cryptography.x509.oid import NameOID
except ImportError:
    sys.exit("This needs the 'cryptography' package:\n\n    pip install cryptography\n")


ALIAS = "sma-campus-track"

# 10000 days, matching the keytool invocation this replaces. Play requires a certificate
# valid past 2033, and the key can never be swapped afterwards: an app signed with a
# different key is a different app to Android and cannot upgrade the old one in place.
VALIDITY_DAYS = 10_000

SUBJECT = x509.Name([
    x509.NameAttribute(NameOID.COMMON_NAME, "SMA Campus Track"),
    x509.NameAttribute(NameOID.ORGANIZATION_NAME, "SMA Technology"),
    x509.NameAttribute(NameOID.COUNTRY_NAME, "PK"),
])


def read_password() -> bytes:
    """Prompt twice, never echo, and refuse anything Android tooling will reject."""
    while True:
        first = getpass.getpass("Choose a keystore password (input hidden): ")

        if len(first) < 6:
            # Java's keystore format enforces this; failing here is clearer than a
            # stack trace from Gradle three steps later.
            print("  Too short. A keystore password must be at least 6 characters.\n")
            continue

        second = getpass.getpass("Type it again to confirm: ")
        if first != second:
            print("  Those did not match. Try again.\n")
            continue

        return first.encode("utf-8")


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent

    # Outside the repository by default. A key inside the working tree is one `git add -A`
    # away from being published, and .gitignore is a weaker guarantee than distance.
    default_dir = repo_root.parent / "sma-campus-track-keys"
    raw = input(f"Where should the keystore be written?\n  [{default_dir}]: ").strip()
    out_dir = Path(raw).expanduser() if raw else default_dir
    out_path = out_dir / "sma-campus-track.jks"

    if out_path.exists():
        print(f"\n{out_path} already exists.")
        print("Refusing to overwrite it: if this is the key the app was signed with,")
        print("replacing it means no existing install can ever be updated again.")
        return 1

    password = read_password()

    print("\nGenerating a 2048-bit RSA key. This takes a moment...")
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)

    now = dt.datetime.now(dt.timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(SUBJECT)
        .issuer_name(SUBJECT)                       # self-signed
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - dt.timedelta(days=1))   # tolerate clock skew
        .not_valid_after(now + dt.timedelta(days=VALIDITY_DAYS))
        .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
        .sign(private_key=key, algorithm=hashes.SHA256())
    )

    blob = pkcs12.serialize_key_and_certificates(
        name=ALIAS.encode("utf-8"),
        key=key,
        cert=certificate,
        cas=None,
        encryption_algorithm=serialization.BestAvailableEncryption(password),
    )

    out_dir.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(blob)

    try:
        os.chmod(out_path, 0o600)
    except OSError:
        pass  # Windows ignores POSIX modes; the file inherits the folder's ACL.

    expires = (now + dt.timedelta(days=VALIDITY_DAYS)).date()

    print(f"\nWritten: {out_path}")
    print(f"  Format      PKCS#12 (what keytool produces by default)")
    print(f"  Alias       {ALIAS}")
    print(f"  Key         RSA 2048")
    print(f"  Expires     {expires}")

    # Prove the file is not inside the repository, rather than asserting it.
    try:
        out_path.relative_to(repo_root)
        inside = True
    except ValueError:
        inside = False

    if inside:
        result = subprocess.run(
            ["git", "check-ignore", "-q", str(out_path)],
            cwd=repo_root, capture_output=True,
        )
        if result.returncode == 0:
            print("\n  The keystore is inside the repository but gitignored.")
        else:
            print("\n  WARNING: the keystore is inside the repository and NOT gitignored.")
            print("  Move it somewhere outside the working tree before committing anything.")
    else:
        print("\n  The keystore is outside the repository and cannot be committed by accident.")

    print(f"""
Back this file up somewhere private and durable, along with the password.
Losing either means the app can never be updated again, only replaced under a new
application id, with every family uninstalling and reinstalling.

Next, to build locally:

  1. Copy mobile/campustrack_app/android/key.properties.template
          to mobile/campustrack_app/android/key.properties
  2. Set:
          storeFile={out_path.as_posix()}
          storeType=PKCS12
          keyAlias={ALIAS}
     and the two password fields to the password you just chose.

To build in CI, add these repository secrets
(GitHub > Settings > Secrets and variables > Actions):

  ANDROID_KEYSTORE_BASE64      base64 of this file, produced with:
                                 python tools/generate-release-key.py --base64
  ANDROID_KEYSTORE_PASSWORD    the password you just chose
  ANDROID_KEY_ALIAS            {ALIAS}
  ANDROID_KEY_PASSWORD         the same password
""")
    return 0


def emit_base64() -> int:
    """Print the keystore base64 encoded, for pasting into a CI secret."""
    raw = input("Path to the keystore: ").strip().strip('"')
    path = Path(raw).expanduser()

    if not path.is_file():
        return print(f"No file at {path}") or 1

    import base64
    print("\nCopy everything between the lines into the ANDROID_KEYSTORE_BASE64 secret:\n")
    print("-" * 60)
    print(base64.b64encode(path.read_bytes()).decode("ascii"))
    print("-" * 60)
    return 0


if __name__ == "__main__":
    sys.exit(emit_base64() if "--base64" in sys.argv else main())
