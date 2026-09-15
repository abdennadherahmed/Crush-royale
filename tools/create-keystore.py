"""Creates the Android release keystore for Crush Royale (run once, by the owner, on their own PC).

    python tools/create-keystore.py

- Asks for a password (typed by you, never stored in the repository).
- Writes a PKCS#12 keystore (RSA 3072, valid 30 years, alias "crushroyale") OUTSIDE the repository, in
  %USERPROFILE%\\CrushRoyale-signing\\, plus a base64 copy to paste into the GitHub secret ANDROID_KEYSTORE_BASE64.
- Prints the SHA-1 / SHA-256 fingerprints needed for Google sign-in (Google Cloud OAuth Android client).

Keep the keystore and its password safe (password manager + a backup copy): without them the game can never be
updated on Google Play. Requires the "cryptography" Python package.
"""
import base64
import datetime
import getpass
import os
import sys
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID

ALIAS = "crushroyale"


def main() -> int:
    out_dir = Path(os.environ.get("USERPROFILE", str(Path.home()))) / "CrushRoyale-signing"
    keystore = out_dir / "crushroyale-release.keystore"
    if keystore.exists():
        print(f"A keystore already exists: {keystore}\nDelete or move it yourself first if you really want a new one.")
        return 1

    password = getpass.getpass("Keystore password (min. 12 characters, not shown): ")
    if len(password) < 12 or password != getpass.getpass("Repeat the password: "):
        print("Passwords are shorter than 12 characters or do not match. Nothing was created.")
        return 1

    key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
    name = x509.Name([
        x509.NameAttribute(NameOID.COMMON_NAME, "Crush Royale"),
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Crush Royale"),
    ])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(days=1))
        .not_valid_after(now + datetime.timedelta(days=365 * 30))
        .sign(key, hashes.SHA256())
    )
    data = pkcs12.serialize_key_and_certificates(
        ALIAS.encode(), key, cert, None, serialization.BestAvailableEncryption(password.encode())
    )

    out_dir.mkdir(parents=True, exist_ok=True)
    keystore.write_bytes(data)
    (out_dir / "ANDROID_KEYSTORE_BASE64.txt").write_text(base64.b64encode(data).decode("ascii"), encoding="ascii")

    der = cert.public_bytes(serialization.Encoding.DER)
    def fingerprint(algorithm) -> str:
        digest = hashes.Hash(algorithm)
        digest.update(der)
        return ":".join(f"{b:02X}" for b in digest.finalize())

    print(f"\nKeystore created: {keystore}")
    print(f"Alias: {ALIAS} (the key password is the same as the keystore password)")
    print(f"SHA-1:   {fingerprint(hashes.SHA1())}")
    print(f"SHA-256: {fingerprint(hashes.SHA256())}")
    print("\nGitHub > Settings > Secrets and variables > Actions, add:")
    print("  ANDROID_KEYSTORE_BASE64 = content of ANDROID_KEYSTORE_BASE64.txt (same folder)")
    print("  ANDROID_KEYSTORE_PASS   = the password you just typed")
    print("Then back up the keystore folder somewhere safe (not in the game repository).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
