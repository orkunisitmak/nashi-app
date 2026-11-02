#!/usr/bin/env python3
"""Bake update trust into a bundle before code signing it. Never reads private keys."""
import base64
import os
from pathlib import Path
import plistlib
import re
import sys
from urllib.parse import urlsplit


def apply_configuration(info_path, configuration_path, environ=None):
    environ = os.environ if environ is None else environ
    with open(info_path, "rb") as stream:
        info = plistlib.load(stream)
    for name, key in [("NASHI_APP_VERSION", "CFBundleShortVersionString"), ("NASHI_BUILD_NUMBER", "CFBundleVersion")]:
        if value := environ.get(name):
            if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){0,2}", value):
                raise ValueError(f"{name} must contain one to three numeric components")
            info[key] = value
    path = Path(configuration_path)
    if path.is_file():
        with path.open("rb") as stream:
            configuration = plistlib.load(stream)
        feed = configuration.get("FeedURL", "")
        key = configuration.get("PublicEDKey", "")
        if not isinstance(key, str):
            raise ValueError("PublicEDKey must be a string")
        address = urlsplit(feed)
        local = configuration.get("LocalDevelopment", False) is True
        loopback = address.hostname in {"localhost", "127.0.0.1", "::1"}
        if not address.hostname or address.username or address.password or address.fragment:
            raise ValueError("FeedURL must be an absolute URL without credentials or a fragment")
        if address.scheme != "https" and not (local and loopback and address.scheme == "http"):
            raise ValueError("FeedURL must use HTTPS; local development HTTP must be loopback only")
        if len(base64.b64decode(key, validate=True)) != 32:
            raise ValueError("PublicEDKey must be a base64-encoded 32-byte Ed25519 public key")
        info["SUFeedURL"] = feed
        info["SUPublicEDKey"] = key
        if configuration.get("GitHubOnly", False) is True:
            match = re.fullmatch(r"/([A-Za-z0-9][A-Za-z0-9-]{0,38})/([A-Za-z0-9_.-]{1,100})/releases/latest/download/appcast\.xml", address.path)
            if address.hostname != "github.com" or address.scheme != "https" or address.query or not match:
                raise ValueError("GitHubOnly requires the repository's HTTPS latest appcast asset URL")
            # No unauthenticated checks before the user saves a connection.
            info.pop("SUFeedURL", None)
            info["NashiSuggestedGitHubRepository"] = "/".join(match.groups())
        else:
            info.pop("NashiSuggestedGitHubRepository", None)
        public_repository=configuration.get("PublicRepository")
        if public_repository:
            if configuration.get("GitHubOnly",False) or feed != "https://github.com/"+public_repository+"/releases/latest/download/appcast.xml":
                raise ValueError("PublicRepository must match the anonymous HTTPS feed")
            if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}",public_repository):
                raise ValueError("Invalid PublicRepository")
            info["NashiPublicUpdateRepository"]=public_repository
        else:
            info.pop("NashiPublicUpdateRepository",None)
        if local:
            if not loopback:
                raise ValueError("LocalDevelopment feeds must use a loopback address")
            info["NashiLocalUpdateFeed"] = True
            info["NSAppTransportSecurity"] = {"NSAllowsLocalNetworking": True}
        else:
            info.pop("NashiLocalUpdateFeed", None)
            # The private GitHub adapter binds only 127.0.0.1. Remote requests
            # still require HTTPS; arbitrary insecure loads remain disallowed.
            info["NSAppTransportSecurity"] = {"NSAllowsLocalNetworking": True}
        print(f"Update feed: {feed}")
    else:
        if environ.get("NASHI_UPDATE_CONFIG"):
            raise FileNotFoundError(f"Update configuration does not exist: {path}")
        info.pop("SUFeedURL", None)
        info.pop("NashiSuggestedGitHubRepository", None)
        key = info.get("SUPublicEDKey", "")
        if key:
            if len(base64.b64decode(key, validate=True)) != 32:
                raise ValueError("The bundled update signing key must be a 32-byte Ed25519 public key")
            print("Update signing key included; connect a GitHub repository in the app.")
        else:
            print("Update controls included; release feed and signing key are not configured yet.")
    with open(info_path, "wb") as stream:
        plistlib.dump(info, stream)


if __name__ == "__main__":
    try:
        apply_configuration(*sys.argv[1:])
    except (ValueError, OSError, plistlib.InvalidFileException) as error:
        sys.exit(str(error))
