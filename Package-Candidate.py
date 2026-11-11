"""Seal the tested local 0.8.4 candidate; never install or publish it."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile


ROOT = Path(__file__).resolve().parent
VERSION = "0.8.4"
BASELINE = ROOT.parent / "SinglePluginCandidate/dist/KellysClientEffects-v0.8.3-CLIENT.zip"
BASELINE_SHA = "CAA9E4AA43AEBEBBD9010651F9DA9919E1CB35948A3B50B71CEDFD3BE151BB02"
STARTUP_SHA = "C016C7C59C59C89BEC0834FF5C4A5028F37F1CABCD968C6FC6AADC30F8453C32"
ARCHIVE_NAME = f"KellysClientEffects-v{VERSION}-CLIENT-CANDIDATE.zip"
MAIN_NAME = "BepInEx/plugins/UnifiedEffects/UnifiedEffects.dll"
STARTUP_NAME = "BepInEx/patchers/UnifiedEffectsStartup.dll"
SOURCE_URL = "https://github.com/Kelly-D-Ripper/Kellys-Client-Effects"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verify_run(directory: Path, payload: dict[str, bytes]) -> dict:
    directory = directory.resolve(strict=True)
    launch_path = directory / "launch.json"
    result_path = directory / "BepInEx/plugins/Probe/result.txt"
    protected_path = directory / "player-data-verification.json"
    launch = read_json(launch_path)
    protected = read_json(protected_path)
    result = result_path.read_text(encoding="utf-8-sig")
    require(re.match(r"\APASS(?:\s|$)", result) is not None, f"No passing result: {result_path}")
    require(re.search(r"(?m)^\s*FAIL\b", result) is None, f"Failure in result: {result_path}")
    require(protected.get("playerPrefsMatchBaseline") is True, f"Player preferences not verified: {directory}")
    require(protected.get("playerFilesUnchanged") is True, f"Player files not verified: {directory}")
    require(not protected.get("changedPlayerFiles", []), f"Changed player files recorded: {directory}")
    recorded = launch.get("componentSha256", {})
    for name, expected in payload.items():
        key = name.removeprefix("BepInEx/")
        require(str(recorded.get(key, "")).upper() == sha(expected), f"Launch used different {key}: {directory}")
        staged = directory / name
        require(staged.is_file() and sha(staged.read_bytes()) == sha(expected), f"Staged artifact differs: {staged}")
    checks = len(re.findall(r"(?m)^PASS [^\r\n]", result))
    require(checks > 0, f"Empty passing result: {directory}")
    return {
        "directory": str(directory),
        "checks": checks,
        "resultSha256": sha(result_path.read_bytes()),
        "launchSha256": sha(launch_path.read_bytes()),
        "playerDataVerificationSha256": sha(protected_path.read_bytes()),
        "disabledBoot": bool(launch.get("disabledBoot", False)),
        "doormanInstalled": "plugins/KellysJOINCHECK.dll" in recorded,
        "noAtmoInstalled": "plugins/NoAtmoFX/NoAtmoFX.dll" in recorded,
        "protectedPlayerFiles": protected.get("checkedPlayerFiles"),
        "playerPrefsMatchBaseline": True,
        "playerFilesUnchanged": True,
    }


def external_links(markdown: str) -> str:
    # The lean archive contains no standalone source/credit files. Keep every link useful.
    markdown = markdown.replace("](" + "CREDITS.md)", "](#credits)")
    return re.sub(
        r"\]\((?!https?://|#)([^)]+)\)",
        lambda match: "](" + SOURCE_URL + "/blob/main/" + match[1] + ")",
        markdown,
    )


def packaged_readme(baseline: zipfile.ZipFile) -> bytes:
    old = baseline.read("README.md").decode("utf-8-sig")
    opening = re.search(r"<details>\r?\n<summary>Third-party licence notices</summary>", old)
    require(opening is not None, "Pinned baseline has no original inline licence notices")
    # The baseline's notices occupy the final details block. Preserve them verbatim.
    notices = old[opening.start():]
    require("</details>" in notices, "Pinned licence block is incomplete")
    intro = (ROOT / "README.md").read_text(encoding="utf-8-sig").rstrip()
    credits = (ROOT / "CREDITS.md").read_text(encoding="utf-8-sig").rstrip()
    require("0.8.4" in intro, "Candidate README does not identify 0.8.4")
    require("\u2014" not in intro + credits, "README/credits must use regular hyphens")
    combined = external_links(intro) + "\n\n" + external_links(credits) + "\n\n"
    combined += (
        "[Source repository](" + SOURCE_URL + ") | "
        "[Adapted canopy-rain shader source and masks](" + SOURCE_URL +
        "/tree/954a4640da16fd81871b598463da8ce294e9bec7/third-party/RainyGlassShader)\n\n"
        "The canopy-rain shader uses LGPL-2.1 material. Its source and masks are linked above. "
        "The original third-party notices follow.\n\n" + notices
    )
    require(notices in combined, "Original notices were not retained")
    return combined.encode("utf-8")


def zip_bytes(payload: dict[str, bytes]) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(payload.items()):
            item = zipfile.ZipInfo(name, date_time=(2026, 11, 11, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    sealed = buffer.getvalue()
    with zipfile.ZipFile(io.BytesIO(sealed)) as archive:
        require(archive.testzip() is None, "ZIP CRC validation failed")
        require(set(archive.namelist()) == {MAIN_NAME, STARTUP_NAME, "README.md"}, "Unexpected archive contents")
        for name, data in payload.items():
            require(archive.read(name) == data, f"ZIP extraction differs: {name}")
    return sealed


def write_sealed(files: dict[Path, bytes]) -> None:
    # Validate the whole output set before creating anything, and never replace existing bytes.
    for path, data in files.items():
        require(not path.exists() or path.read_bytes() == data, f"Refusing to overwrite a different sealed file: {path}")
    for path, data in files.items():
        if path.exists():
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as handle:
            handle.write(data)
        require(path.read_bytes() == data, f"Sealed write verification failed: {path}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", nargs="+", required=True, type=Path, metavar="DIRECTORY",
                        help="Passing isolated mission evidence directories for these exact runtime bytes")
    args = parser.parse_args()
    baseline_bytes = BASELINE.read_bytes()
    require(sha(baseline_bytes) == BASELINE_SHA, "Pinned public 0.8.3 ZIP hash mismatch")
    main_path = ROOT / "Weather/bin/Release/netstandard2.1/BoscaliUnifiedWeather.dll"
    startup_path = ROOT / "Startup/bin/Release/net472/UnifiedEffectsStartup.dll"
    payload = {MAIN_NAME: main_path.read_bytes(), STARTUP_NAME: startup_path.read_bytes()}
    require(sha(payload[STARTUP_NAME]) == STARTUP_SHA, "Startup helper changed unexpectedly")
    directories = sorted({p.resolve(strict=True) for p in args.evidence}, key=str)
    require(len(directories) == len(args.evidence), "Duplicate evidence directories")
    runs = [verify_run(p, payload) for p in directories]
    require(any(not run["disabledBoot"] for run in runs), "At least one enabled-pack mission test is required")
    with zipfile.ZipFile(io.BytesIO(baseline_bytes)) as baseline:
        require(payload[STARTUP_NAME] == baseline.read(STARTUP_NAME), "Startup does not match the released helper")
        payload["README.md"] = packaged_readme(baseline)
    archive = zip_bytes(payload)
    manifest = {
        "version": VERSION,
        "status": "local candidate",
        "archive": ARCHIVE_NAME,
        "sha256": sha(archive),
        "size": len(archive),
        "installed": False,
        "published": False,
        "databaseImpact": "none",
        "configurationImpact": "Additive optional settings default off. Existing config identities and values retained. No config files are included or overwritten.",
        "baseline": {"archive": BASELINE.name, "sha256": BASELINE_SHA},
        "sourceBranch": "codex/client-effects-0.8.4",
        "sourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "sourceDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()),
        "files": [{"path": name, "size": len(data), "sha256": sha(data)} for name, data in sorted(payload.items())],
        "missionTests": runs,
        "limits": [
            "Live multiplayer acceptance against an unmodified server/client remains open.",
            "Sustained frame-time and performance acceptance remains open; no FPS improvement claimed.",
            "Human flight, audio and visual acceptance across aircraft/airfields remains open.",
            "NOAtmoFX remains a separately downloaded optional add-on and is not included.",
        ],
    }
    dist = ROOT / "dist"
    manifest_bytes = (json.dumps(manifest, indent=2) + "\n").encode("utf-8")
    sums = (sha(archive) + "  " + ARCHIVE_NAME + "\n").encode("ascii")
    write_sealed({dist / ARCHIVE_NAME: archive, dist / "manifest.json": manifest_bytes, dist / "SHA256SUMS.txt": sums})
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
