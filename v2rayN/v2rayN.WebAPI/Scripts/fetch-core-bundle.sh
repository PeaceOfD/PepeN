#!/bin/sh
set -eu

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <linux-x64|linux-arm64|linux-riscv64|linux-loongarch64> <destination-bin-directory>" >&2
  exit 2
fi

rid="$1"
destination="$2"

case "$rid" in
  linux-x64) bundle="v2rayN-linux-64" ;;
  linux-arm64) bundle="v2rayN-linux-arm64" ;;
  linux-riscv64) bundle="v2rayN-linux-riscv64" ;;
  linux-loongarch64) bundle="v2rayN-linux-loong64" ;;
  *)
    echo "Unsupported runtime identifier: $rid" >&2
    exit 2
    ;;
esac

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT HUP INT TERM

bundle_url="https://raw.githubusercontent.com/2dust/v2rayN-core-bin/refs/heads/master/$bundle.zip"
curl --fail --location --retry 3 --retry-delay 2 --silent --show-error \
  --output "$temporary_directory/core-bundle.zip" "$bundle_url"
unzip -q "$temporary_directory/core-bundle.zip" -d "$temporary_directory/unpacked"

source_bin="$temporary_directory/unpacked/$bundle/bin"
# These files are sanity checks for the current upstream payload, never an allowlist.
for required_file in \
  xray/xray \
  sing_box/sing-box \
  mihomo/mihomo \
  Country.mmdb \
  geoip-only-cn-private.dat \
  geoip.dat \
  geoip.metadb \
  geosite.dat \
  srss/geoip-cn.srs \
  srss/geosite-cn.srs \
  srss/geosite-category-ads-all.srs; do
  if [ ! -f "$source_bin/$required_file" ]; then
    echo "Core bundle is missing required asset: $required_file" >&2
    exit 1
  fi
done
# The cronet payload is only present in the x64/arm64 bundles.
if [ "$rid" = "linux-x64" ] || [ "$rid" = "linux-arm64" ]; then
  if [ ! -f "$source_bin/sing_box/libcronet.so" ]; then
    echo "Core bundle is missing required asset: sing_box/libcronet.so" >&2
    exit 1
  fi
fi

mkdir -p "$destination"
# Preserve the complete official bin/ tree, including future upstream assets and Cores.
cp -a "$source_bin/." "$destination/"

# PattN: Xray-core from patterniha/Xray-core and the geo files from Chocolate4U, as in PattN's desktop packages
case "$rid" in
  linux-x64) xray_asset="Xray-linux-64.zip" ;;
  linux-arm64) xray_asset="Xray-linux-arm64-v8a.zip" ;;
  linux-riscv64) xray_asset="Xray-linux-riscv64.zip" ;;
  linux-loongarch64) xray_asset="Xray-linux-loong64.zip" ;;
esac
curl --fail --location --retry 3 --retry-delay 2 --silent --show-error \
  --output "$temporary_directory/xray-core.zip" "https://github.com/patterniha/Xray-core/releases/latest/download/$xray_asset"
unzip -q -o "$temporary_directory/xray-core.zip" xray -d "$temporary_directory/xray-core"
cp "$temporary_directory/xray-core/xray" "$destination/xray/xray"
for geo_file in geosite.dat geoip.dat; do
  curl --fail --location --retry 3 --retry-delay 2 --silent --show-error \
    --output "$destination/$geo_file" "https://github.com/Chocolate4U/Iran-v2ray-rules/releases/latest/download/$geo_file"
done
mkdir -p "$destination/srss"
for rule_set in geosite-category-ir geoip-ir; do
  curl --fail --location --retry 3 --retry-delay 2 --silent --show-error \
    --output "$destination/srss/$rule_set.srs" "https://raw.githubusercontent.com/chocolate4u/Iran-sing-box-rules/rule-set/$rule_set.srs"
done

chmod 755 \
  "$destination/xray/xray" \
  "$destination/sing_box/sing-box" \
  "$destination/mihomo/mihomo"
if [ -f "$destination/sing_box/libcronet.so" ]; then
  chmod 644 "$destination/sing_box/libcronet.so"
fi

printf 'Integrated complete upstream %s Core/data bin payload into %s\n' "$rid" "$destination"
printf 'Replaced Xray-core with patterniha/Xray-core and the geo files with Chocolate4U'"'"'s\n'
