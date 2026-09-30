#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
pack_root="$repo_root/docs/release/store-submission"
mobile_root="$repo_root/src/QueenZone.Mobile"
icon_source="$mobile_root/assets/icon.png"
cinzel_semibold="$mobile_root/node_modules/@expo-google-fonts/cinzel/600SemiBold/Cinzel_600SemiBold.ttf"
cinzel_medium="$mobile_root/node_modules/@expo-google-fonts/cinzel/500Medium/Cinzel_500Medium.ttf"
inter_medium="$mobile_root/node_modules/@expo-google-fonts/inter/500Medium/Inter_500Medium.ttf"

for required in magick git; do
  command -v "$required" >/dev/null || {
    echo "$required is required." >&2
    exit 1
  }
done

for required_file in "$icon_source" "$cinzel_semibold" "$cinzel_medium" "$inter_medium"; do
  [[ -f "$required_file" ]] || {
    echo "Missing $required_file. Run npm ci in src/QueenZone.Mobile first." >&2
    exit 1
  }
done

mkdir -p \
  "$pack_root/apple/assets/icon" \
  "$pack_root/google-play/assets/icon" \
  "$pack_root/google-play/assets/feature-graphic"

# Apple: 1024px, 8-bit truecolour PNG without alpha.
magick "$icon_source" \
  -background '#111111' -alpha remove -alpha off -depth 8 -strip \
  -define png:color-type=2 \
  "$pack_root/apple/assets/icon/QueenZone-AppStore-1024.png"

# Google Play icon: 512px, 8-bit RGBA (32-bit PNG). The source alpha is fully
# opaque, but the channel is retained to satisfy Play's documented encoding.
magick "$icon_source" \
  -filter Lanczos -resize 512x512 -depth 8 -strip \
  -define png:color-type=6 \
  "$pack_root/google-play/assets/icon/QueenZone-PlayStore-512.png"

# Google Play feature graphic: 1024x500, 8-bit truecolour (24-bit), no alpha.
magick -size 1024x500 gradient:'#6B1F33-#B89A4A' \
  -rotate 90 -resize 1024x500\! -colorspace sRGB \
  -fill 'rgba(17,17,17,0.20)' -stroke none \
  -draw 'rectangle 0,0 1024,500' \
  -fill none -stroke 'rgba(255,255,255,0.13)' -strokewidth 2 \
  -draw 'circle 80,250 310,250 circle 944,250 714,250' \
  -stroke '#F7F5F0' -strokewidth 2 \
  -draw 'line 430,135 594,135' \
  -stroke none -fill '#FFFFFF' -font "$cinzel_semibold" -pointsize 70 -gravity center \
  -annotate +0-12 'QUEENZONE' \
  -fill '#F7F5F0' -font "$cinzel_medium" -pointsize 23 -gravity center \
  -annotate +0+62 'THE ARCHIVE · PRESERVED' \
  -fill '#F7F5F0' -font "$inter_medium" -pointsize 17 -gravity center \
  -annotate +0+118 'HISTORY  •  NEWS  •  PHOTOGRAPHY  •  COMMUNITY' \
  -depth 8 -alpha off -strip -define png:color-type=2 \
  "$pack_root/google-play/assets/feature-graphic/QueenZone-FeatureGraphic-1024x500.png"

magick identify -format '%f %wx%h opaque=%[opaque] depth=%z type=%[type]\n' \
  "$pack_root/apple/assets/icon/QueenZone-AppStore-1024.png" \
  "$pack_root/google-play/assets/icon/QueenZone-PlayStore-512.png" \
  "$pack_root/google-play/assets/feature-graphic/QueenZone-FeatureGraphic-1024x500.png"

