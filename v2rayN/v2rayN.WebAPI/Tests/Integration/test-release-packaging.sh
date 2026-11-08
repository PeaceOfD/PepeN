#!/usr/bin/env bash
# Release packaging dry-run: validates Linux/Windows full-install and app-only ZIP boundaries
# plus the WebAPI-update.json manifest using small fixtures, without publishing anything.
set -euo pipefail

web_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
asset_tool="$web_root/Scripts/web-release-assets.py"
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT HUP INT TERM

make_publish_fixture() {
  local rid="$1"
  local publish="$temporary/publish-$rid"
  local app_executable="v2rayN.WebAPI" xray="bin/xray/xray" sing_box="bin/sing_box/sing-box" mihomo="bin/mihomo/mihomo"
  local pe_machine=""
  case "$rid" in
    win-x64) pe_machine="0x8664" ;;
    win-arm64) pe_machine="0xAA64" ;;
  esac
  if [[ -n "$pe_machine" ]]; then
    app_executable="v2rayN.WebAPI.exe"
    xray="bin/xray/xray.exe"
    sing_box="bin/sing_box/sing-box.exe"
    mihomo="bin/mihomo/mihomo.exe"
  fi
  mkdir -p "$publish/bin/xray" "$publish/bin/sing_box" "$publish/bin/mihomo" "$publish/bin/srss"
  if [[ -n "$pe_machine" ]]; then
    python3 - "$publish/$app_executable" "$publish/$xray" "$publish/$sing_box" "$publish/$mihomo" "$pe_machine" <<'PY'
from pathlib import Path
import struct
import sys

names = sys.argv[1:-1]
machine = int(sys.argv[-1], 16)
image = bytearray(512)
image[:2] = b"MZ"
struct.pack_into("<I", image, 0x3C, 128)
image[128:132] = b"PE\0\0"
struct.pack_into("<HHH", image, 132, machine, 1, 0)
struct.pack_into("<H", image, 148, 240)
struct.pack_into("<H", image, 150, 0x0002)
struct.pack_into("<H", image, 152, 0x020B)
for name in names:
    Path(name).write_bytes(image)
PY
  else
    printf '#!/bin/sh\nexit 0\n' > "$publish/$app_executable"
    chmod 755 "$publish/$app_executable"
    for executable in "$xray" "$sing_box" "$mihomo"; do
      printf '#!/bin/sh\nexit 0\n' > "$publish/$executable"
      chmod 755 "$publish/$executable"
    done
  fi
  # Simulate accidental local environment files in the publish tree. Only the reviewed
  # repository template may be copied into a full install ZIP.
  printf 'V2RAYN_WEB_API_KEY=fixture-secret\n' > "$publish/.env"
  printf 'unreviewed template\n' > "$publish/.env.example"
  local assets=(bin/geosite.dat bin/geoip.dat bin/geoip.metadb bin/Country.mmdb bin/srss/geosite-category-ads-all.srs)
  if [[ "$rid" == linux-x64 || "$rid" == linux-arm64 ]]; then assets+=(bin/sing_box/libcronet.so); fi
  for asset in "${assets[@]}"; do
    printf 'asset\n' > "$publish/$asset"
  done
  # PattN: the Aether core and its two programs, their license texts, the archive's LICENSES folder and the notice in
  # bin/aether, and Psiphon's server list in bin (package-aether.sh), where patterniha/Aether publishes an archive
  case "$rid" in
    linux-x64|linux-arm64|win-x64|win-arm64)
      local exe="" program
      if [[ -n "$pe_machine" ]]; then exe=".exe"; fi
      mkdir -p "$publish/bin/aether/LICENSES"
      for program in aether lyrebird psiphon-tunnel-core; do
        printf '#!/bin/sh\nexit 0\n' > "$publish/bin/aether/$program$exe"
        chmod 755 "$publish/bin/aether/$program$exe"
      done
      for asset in bin/aether/NOTICE.txt bin/aether/AGPL-3.0.txt bin/aether/GPL-3.0.txt bin/aether/lyrebird-LICENSE.txt \
        bin/aether/LICENSES/THIRD-PARTY-NOTICES.txt bin/psiphon_servers.dat bin/psiphon_servers.stamp; do
        printf 'asset\n' > "$publish/$asset"
      done
      ;;
  esac
  # User data that must never leak into the app-only self-update package.
  mkdir -p "$publish/guiConfigs" "$publish/guiLogs" "$publish/WebAPIData"
  printf 'config\n' > "$publish/guiConfigs/guiNConfig.json"
  printf 'log\n' > "$publish/guiLogs/app.log"
  printf 'auth\n' > "$publish/WebAPIData/WebAPI-auth.json"
  python3 - "$publish/v2rayN.WebAPI.build.json" "$rid" <<'PY'
import json
import sys

path, rid = sys.argv[1:]
with open(path, "w", encoding="utf-8") as handle:
    json.dump({
        "product": "v2rayN.WebAPI",
        "version": "7.25.3",
        "commit": "0123456789abcdef",
        "buildDate": "2026-09-28T00:00:00Z",
        "rid": rid,
    }, handle, separators=(",", ":"))
    handle.write("\n")
PY
}

for rid in linux-x64 linux-arm64 linux-riscv64 linux-loongarch64 win-x64 win-arm64; do
  make_publish_fixture "$rid"
  python3 "$asset_tool" package --rid "$rid" --publish "$temporary/publish-$rid" --dist "$temporary/dist"
done

full_x64="$(python3 "$asset_tool" get --rid linux-x64 --field full)"
full_arm64="$(python3 "$asset_tool" get --rid linux-arm64 --field full)"
full_riscv64="$(python3 "$asset_tool" get --rid linux-riscv64 --field full)"
full_loong64="$(python3 "$asset_tool" get --rid linux-loongarch64 --field full)"
update_x64="$(python3 "$asset_tool" get --rid linux-x64 --field update)"
update_arm64="$(python3 "$asset_tool" get --rid linux-arm64 --field update)"
update_riscv64="$(python3 "$asset_tool" get --rid linux-riscv64 --field update)"
update_loong64="$(python3 "$asset_tool" get --rid linux-loongarch64 --field update)"
full_windows="$(python3 "$asset_tool" get --rid win-x64 --field full)"
update_windows="$(python3 "$asset_tool" get --rid win-x64 --field update)"
full_windows_arm64="$(python3 "$asset_tool" get --rid win-arm64 --field full)"
update_windows_arm64="$(python3 "$asset_tool" get --rid win-arm64 --field update)"

for asset in "$full_x64" "$full_arm64" "$full_riscv64" "$full_loong64" \
  "$update_x64" "$update_arm64" "$update_riscv64" "$update_loong64" \
  "$full_windows" "$update_windows" "$full_windows_arm64" "$update_windows_arm64"; do
  test -s "$temporary/dist/$asset"
done
test "$full_x64" = "PattN-linux-64-WebAPI.zip"
test "$full_arm64" = "PattN-linux-arm64-WebAPI.zip"
test "$full_riscv64" = "PattN-linux-riscv64-WebAPI.zip"
test "$full_loong64" = "PattN-linux-loong64-WebAPI.zip"
test "$update_x64" = "PattN-linux-64-WebAPI-update.zip"
test "$update_arm64" = "PattN-linux-arm64-WebAPI-update.zip"
test "$update_riscv64" = "PattN-linux-riscv64-WebAPI-update.zip"
test "$update_loong64" = "PattN-linux-loong64-WebAPI-update.zip"
test "$full_windows" = "PattN-windows-64-WebAPI.zip"
test "$update_windows" = "PattN-windows-64-WebAPI-update.zip"
test "$full_windows_arm64" = "PattN-windows-arm64-WebAPI.zip"
test "$update_windows_arm64" = "PattN-windows-arm64-WebAPI-update.zip"

python3 - "$temporary/dist/$full_x64" "$temporary/dist/$update_x64" "$temporary/dist/$full_windows" "$temporary/dist/$update_windows" "$web_root/.env.example" <<'PY'
from pathlib import Path
import sys
import zipfile

full, update, full_windows, update_windows, env_example_source = sys.argv[1:]
with zipfile.ZipFile(full) as archive:
    names = archive.namelist()
    env_example_data = archive.read(".env.example")
for required in ("v2rayN.WebAPI", "v2rayN.WebAPI.build.json", ".env.example", "bin/xray/xray"):
    assert required in names, required
assert names.count(".env.example") == 1, names
assert env_example_data == Path(env_example_source).read_bytes()
assert not any(".env" in name.split("/") for name in names), names
assert not any(".env.example" in name.split("/") and name != ".env.example" for name in names), names
with zipfile.ZipFile(update) as archive:
    update_names = set(archive.namelist())
assert update_names == {"v2rayN.WebAPI", "v2rayN.WebAPI.build.json"}, update_names
assert not any(segment in (".env", ".env.example") for name in update_names for segment in name.split("/")), update_names
assert not any(name.startswith(("bin/", "guiConfigs/", "guiLogs/", "WebAPIData/")) for name in update_names), update_names
with zipfile.ZipFile(full_windows) as archive:
    windows_names = archive.namelist()
    assert "v2rayN.WebAPI.exe" in windows_names, windows_names
    assert "v2rayN.WebAPI.build.json" in windows_names, windows_names
    assert "bin/xray/xray.exe" in windows_names, windows_names
    assert archive.read(".env.example") == Path(env_example_source).read_bytes()
    assert not any(name.split("/")[-1] == ".env" for name in windows_names), windows_names
with zipfile.ZipFile(update_windows) as archive:
    windows_update_names = set(archive.namelist())
assert windows_update_names == {"v2rayN.WebAPI.exe", "v2rayN.WebAPI.build.json"}, windows_update_names
process = __import__("subprocess").run(["unzip", "-l", update], capture_output=True, text=True)
assert process.returncode == 0, process.stderr

web_root = Path(env_example_source).parent
web_ignore = (web_root / ".gitignore").read_text(encoding="utf-8").splitlines()
container_ignore = (web_root / "Containerfile.dockerignore").read_text(encoding="utf-8").splitlines()
assert ".env" in web_ignore, web_ignore
assert ".env" in container_ignore and "**/.env" in container_ignore, container_ignore
print("ZIP boundary assertions passed")
PY

# PattN: the full packages carry the Aether core (executable outside Windows), its license texts and notice, the
# archive's LICENSES folder and Psiphon's server list; riscv64 and loongarch64 have no Aether archive to carry
python3 - "$temporary/dist/$full_x64" "$temporary/dist/$full_arm64" "$temporary/dist/$full_windows" \
  "$temporary/dist/$full_windows_arm64" "$temporary/dist/$full_riscv64" "$temporary/dist/$full_loong64" <<'PY'
import os
import stat
import sys
import zipfile

linux = sys.argv[1:3]
windows = sys.argv[3:5]
exotic = sys.argv[5:7]
texts = ("bin/aether/NOTICE.txt", "bin/aether/AGPL-3.0.txt", "bin/aether/GPL-3.0.txt",
         "bin/aether/lyrebird-LICENSE.txt", "bin/aether/LICENSES/THIRD-PARTY-NOTICES.txt",
         "bin/psiphon_servers.dat", "bin/psiphon_servers.stamp")
for path in linux + windows:
    exe = ".exe" if path in windows else ""
    with zipfile.ZipFile(path) as archive:
        infos = {info.filename: info for info in archive.infolist()}
    for name in texts:
        assert name in infos, (path, name)
    for program in ("aether", "lyrebird", "psiphon-tunnel-core"):
        info = infos.get("bin/aether/" + program + exe)
        assert info is not None, (path, program)
        # The packager keeps the file's mode; only a POSIX file system has the execute bits to keep (CI runs on Linux).
        if not exe and os.name == "posix":
            assert (info.external_attr >> 16) & stat.S_IXUSR, (path, program, oct(info.external_attr >> 16))
for path in exotic:
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
    assert not any(name.startswith("bin/aether/") or name.startswith("bin/psiphon_servers.") for name in names), names
print("Aether bundle assertions passed")
PY

# PattN: a full package without one of the Aether files, or with a core that cannot run, is refused
refuse_without() {
  local rid="$1" missing="$2" message="$3"
  local case_root="$temporary/without-${rid}-${missing//\//_}"
  cp -a "$temporary/publish-$rid" "$case_root"
  rm -f "$case_root/$missing"
  if python3 "$asset_tool" package --rid "$rid" --publish "$case_root" --dist "$case_root-dist" 2>"$case_root.log"; then
    echo "Expected a $rid package without $missing to be rejected" >&2
    exit 1
  fi
  if ! grep -qF "$message: $missing" "$case_root.log"; then
    echo "The $rid package without $missing was rejected for another reason" >&2
    cat "$case_root.log" >&2
    exit 1
  fi
}
for missing in bin/aether/aether bin/aether/lyrebird bin/aether/psiphon-tunnel-core bin/aether/NOTICE.txt \
  bin/aether/AGPL-3.0.txt bin/aether/GPL-3.0.txt bin/aether/lyrebird-LICENSE.txt bin/psiphon_servers.dat \
  bin/psiphon_servers.stamp; do
  refuse_without linux-x64 "$missing" "full package is missing required file"
done
for missing in bin/aether/aether.exe bin/aether/psiphon-tunnel-core.exe bin/aether/lyrebird-LICENSE.txt; do
  refuse_without win-arm64 "$missing" "full package is missing required file"
done
cp -a "$temporary/publish-linux-arm64" "$temporary/aether-not-executable"
chmod 644 "$temporary/aether-not-executable/bin/aether/psiphon-tunnel-core"
# Where the file system has no execute bits (Git Bash on Windows), os.access cannot tell; CI runs on Linux.
if ! python3 -c 'import os, sys; sys.exit(0 if os.access(sys.argv[1], os.X_OK) else 1)' \
  "$temporary/aether-not-executable/bin/aether/psiphon-tunnel-core"; then
  if python3 "$asset_tool" package --rid linux-arm64 --publish "$temporary/aether-not-executable" \
    --dist "$temporary/aether-not-executable-dist" 2>"$temporary/aether-not-executable.log"; then
    echo "Expected a linux-arm64 package whose psiphon-tunnel-core cannot run to be rejected" >&2
    exit 1
  fi
  grep -qF "full package is missing required executable: bin/aether/psiphon-tunnel-core" "$temporary/aether-not-executable.log"
fi

python3 "$web_root/Scripts/web-update-manifest.py" write \
  --version 7.25.3 \
  --repository patterniha/PattN \
  --commit 0123456789abcdef \
  --build-date 2026-09-28T00:00:00Z \
  --dist "$temporary/dist" \
  --output "$temporary/dist/WebAPI-update.json"
python3 "$web_root/Scripts/web-update-manifest.py" verify \
  --manifest "$temporary/dist/WebAPI-update.json" \
  --dist "$temporary/dist" \
  --repository patterniha/PattN \
  --version 7.25.3

if python3 "$web_root/Scripts/web-update-manifest.py" verify \
  --manifest "$temporary/dist/WebAPI-update.json" \
  --dist "$temporary/dist" \
  --repository patterniha/PattN \
  --version 7.25.4 2>/dev/null; then
  echo "Expected the wrong requested release version to be rejected" >&2
  exit 1
fi

python3 - "$temporary/dist/WebAPI-update.json" "$update_x64" "$update_arm64" "$update_riscv64" "$update_loong64" "$update_windows" "$update_windows_arm64" <<'PY'
import hashlib
import json
import sys
from pathlib import Path

manifest_path = Path(sys.argv[1])
asset_x64, asset_arm64, asset_riscv64, asset_loong64, asset_windows, asset_windows_arm64 = sys.argv[2:]
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
assert manifest["product"] == "v2rayN.WebAPI", manifest
assert manifest["version"] == "7.25.3", manifest
assert manifest["commit"] == "0123456789abcdef", manifest
assert manifest["buildDate"] == "2026-09-28T00:00:00Z", manifest
expected = {
    "linux-x64": asset_x64,
    "linux-arm64": asset_arm64,
    "linux-riscv64": asset_riscv64,
    "linux-loongarch64": asset_loong64,
    "win-x64": asset_windows,
    "win-arm64": asset_windows_arm64,
}
packages = {package["rid"]: package for package in manifest["packages"]}
assert set(packages) == set(expected), packages
for rid, asset in expected.items():
    package = packages[rid]
    url = f"https://github.com/patterniha/PattN/releases/download/7.25.3/{asset}"
    assert asset.endswith("-WebAPI-update.zip"), asset
    assert package["asset"] == asset, package
    assert package["url"] == url, package
    data = (manifest_path.parent / asset).read_bytes()
    assert package["sha256"] == hashlib.sha256(data).hexdigest(), package
    assert package["size"] == len(data), package
print("WebAPI-update.json assertions passed")
PY

# A dev identity must never become a release manifest.
if python3 "$web_root/Scripts/web-update-manifest.py" write \
  --version 7.25.3-dev \
  --repository patterniha/PattN \
  --commit 0123456789abcdef \
  --build-date 2026-09-28T00:00:00Z \
  --dist "$temporary/dist" \
  --output "$temporary/dev-update.json" 2>/dev/null; then
  echo "Expected the dev identity manifest to be rejected" >&2
  exit 1
fi
test ! -e "$temporary/dev-update.json"

# A tampered asset must fail SHA-256 verification.
cp -a "$temporary/dist" "$temporary/tampered"
printf 'tampered' >> "$temporary/tampered/$update_x64"
cp -a "$temporary/dist/WebAPI-update.json" "$temporary/tampered/WebAPI-update.json"
if python3 "$web_root/Scripts/web-update-manifest.py" verify \
  --manifest "$temporary/tampered/WebAPI-update.json" \
  --dist "$temporary/tampered" \
  --repository patterniha/PattN 2>/dev/null; then
  echo "Expected the tampered manifest to be rejected" >&2
  exit 1
fi

# The embedded v2rayN.WebAPI.build.json must match the manifest, and the app-only ZIP boundary
# must reject forbidden files, links, duplicates, and path traversal. Each variant regenerates
# the manifest from the tampered archive so SHA-256 and size stay self-consistent and only the
# identity/layout cross-check can fail.
python3 - "$temporary/dist" "$temporary/identity-tamper" "$update_x64" <<'PY'
import json
import shutil
import sys
import warnings
import zipfile
from pathlib import Path

warnings.filterwarnings("ignore", message="Duplicate name:.*")

dist = Path(sys.argv[1])
out_root = Path(sys.argv[2])
source = dist / sys.argv[3]
identity_name = "v2rayN.WebAPI.build.json"
fixed_time = (1980, 1, 1, 0, 0, 0)

with zipfile.ZipFile(source) as archive:
    base = [(info, archive.read(info)) for info in archive.infolist()]

identity = json.loads(next(data for info, data in base if info.filename == identity_name))
identity_data = next(data for info, data in base if info.filename == identity_name)
untouched = [path for path in dist.iterdir() if path.is_file() and path.name.endswith(".zip") and path != source]


def write_variant(name, transform):
    target = out_root / name
    target.mkdir(parents=True, exist_ok=True)
    for path in untouched:
        shutil.copy2(path, target / path.name)
    with zipfile.ZipFile(target / source.name, "w", zipfile.ZIP_DEFLATED) as archive:
        for info, data in transform(list(base)):
            archive.writestr(info, data)


def identity_transform(field=None, value=None, drop=False, invalid=False):
    def transform(entries):
        result = []
        for info, data in entries:
            if info.filename != identity_name:
                result.append((info, data))
                continue
            if drop:
                continue
            if invalid:
                result.append((info, b"{ not valid json"))
                continue
            changed = dict(identity)
            changed[field] = value
            result.append((info, json.dumps(changed, separators=(",", ":")).encode("utf-8")))
        return result
    return transform


def appended(name, data, symlink=False):
    def transform(entries):
        info = zipfile.ZipInfo(name, date_time=fixed_time)
        info.create_system = 3
        file_type = 0o120000 if symlink else 0o100000
        info.external_attr = (file_type | 0o755) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        return entries + [(info, data)]
    return transform


write_variant("version", identity_transform(field="version", value="7.25.4"))
write_variant("commit", identity_transform(field="commit", value="ffffffffffffffff"))
write_variant("build-date", identity_transform(field="buildDate", value="2026-09-29T00:00:00Z"))
write_variant("rid", identity_transform(field="rid", value="linux-arm64"))
write_variant("missing-identity", identity_transform(drop=True))
write_variant("invalid-json", identity_transform(invalid=True))
write_variant("embedded-bin", appended("bin/xray/xray", b"#!/bin/sh\nexit 0\n"))
write_variant("symlink", appended("extra/vendor.dat", b"/etc/passwd", symlink=True))
write_variant("duplicate", appended(identity_name, identity_data))
write_variant("traversal", appended("../outside.txt", b"nope"))
print(f"prepared identity tamper variants in {out_root}")
PY

for variant in \
  version commit build-date rid missing-identity invalid-json \
  embedded-bin symlink duplicate traversal; do
  if python3 "$web_root/Scripts/web-update-manifest.py" write \
    --version 7.25.3 \
    --repository patterniha/PattN \
    --commit 0123456789abcdef \
    --build-date 2026-09-28T00:00:00Z \
    --dist "$temporary/identity-tamper/$variant" \
    --output "$temporary/identity-tamper/$variant/WebAPI-update.json" \
    2>"$temporary/identity-tamper/$variant.log"; then
    echo "Expected the $variant identity tamper to be rejected" >&2
    exit 1
  fi
  if ! grep -q "app-only archive $update_x64" "$temporary/identity-tamper/$variant.log"; then
    echo "The $variant identity tamper was rejected without naming the app-only archive" >&2
    cat "$temporary/identity-tamper/$variant.log" >&2
    exit 1
  fi
done

# PattN: release tags are x.y.z-P<n>; any other suffix is not a release identity
python3 - "$web_root/Scripts/web-update-manifest.py" <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("web_update_manifest", sys.argv[1])
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)
for accepted in ("7.25.6", "7.25.6-P35", "7.25.6-P100"):
    assert manifest.VERSION_RE.match(accepted), accepted
for rejected in ("7.25.6-P", "7.25.6-P0", "7.25.6-P01", "7.25.6-dev", "7.25.6-P35-dev", "v7.25.6-P35"):
    assert not manifest.VERSION_RE.match(rejected), rejected
print("PattN release tag assertions passed")
PY

# PattN: without exotic_arch, the riscv64 and loongarch64 packages are not built, and the manifest lists the others
cp -a "$temporary/dist" "$temporary/without-exotic"
rm -f "$temporary/without-exotic/$full_riscv64" "$temporary/without-exotic/$update_riscv64" \
  "$temporary/without-exotic/$full_loong64" "$temporary/without-exotic/$update_loong64" \
  "$temporary/without-exotic/WebAPI-update.json"
python3 "$web_root/Scripts/web-update-manifest.py" write \
  --version 7.25.3 \
  --repository patterniha/PattN \
  --commit 0123456789abcdef \
  --build-date 2026-09-28T00:00:00Z \
  --dist "$temporary/without-exotic" \
  --output "$temporary/without-exotic/WebAPI-update.json"
python3 - "$temporary/without-exotic/WebAPI-update.json" <<'PY'
import json
import sys

manifest = json.load(open(sys.argv[1], encoding="utf-8"))
rids = {package["rid"] for package in manifest["packages"]}
assert rids == {"linux-x64", "linux-arm64", "win-x64", "win-arm64"}, rids
print("Manifest without the exotic_arch packages passed")
PY
# A required package is still required.
rm -f "$temporary/without-exotic/$update_arm64"
if python3 "$web_root/Scripts/web-update-manifest.py" write \
  --version 7.25.3 \
  --repository patterniha/PattN \
  --commit 0123456789abcdef \
  --build-date 2026-09-28T00:00:00Z \
  --dist "$temporary/without-exotic" \
  --output "$temporary/without-exotic/missing-arm64.json" 2>/dev/null; then
  echo "Expected a release without the linux-arm64 update package to be rejected" >&2
  exit 1
fi
# Half of an optional pair is an error too.
cp -a "$temporary/dist/$update_riscv64" "$temporary/without-exotic/$update_riscv64"
cp -a "$temporary/dist/$update_arm64" "$temporary/without-exotic/$update_arm64"
if python3 "$web_root/Scripts/web-update-manifest.py" write \
  --version 7.25.3 \
  --repository patterniha/PattN \
  --commit 0123456789abcdef \
  --build-date 2026-09-28T00:00:00Z \
  --dist "$temporary/without-exotic" \
  --output "$temporary/without-exotic/half-riscv64.json" 2>/dev/null; then
  echo "Expected a release with only the riscv64 update package to be rejected" >&2
  exit 1
fi

printf 'Release packaging dry-run passed\n'
