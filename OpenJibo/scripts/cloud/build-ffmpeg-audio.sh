#!/bin/sh
# Opt-in candidate only. Keep exact source and build instructions with the binary.
set -eu
jobs=${FFMPEG_BUILD_JOBS:-2}
case "$jobs" in ''|*[!0-9]*) echo 'FFMPEG_BUILD_JOBS must be 1..32' >&2; exit 1;; esac
test "$jobs" -ge 1 && test "$jobs" -le 32

version=6.1.6
fingerprint=FCF986EA15E6E293A5644F10B4322F04D67658D8
evidence=/usr/share/openjibo-ffmpeg
mkdir -p "$evidence"
cd "$evidence"
fetch() {
    curl --proto '=https' --tlsv1.2 --fail --silent --show-error --max-time 120 "$1" -o "$2"
}
fetch "https://ffmpeg.org/releases/ffmpeg-$version.tar.xz" "ffmpeg-$version.tar.xz"
fetch "https://ffmpeg.org/releases/ffmpeg-$version.tar.xz.asc" "ffmpeg-$version.tar.xz.asc"
fetch https://ffmpeg.org/ffmpeg-devel.asc ffmpeg-devel.asc
printf '%s\n' \
    'd4fcb164028dd3beee5d92c0ac72e46aac6973c75ea12dc14de07bf8f407370a  ffmpeg-6.1.6.tar.xz' \
    'd723aad27d00de29dc98a8718002c05cb4293eb2f6240baac70cf066a5607b88  ffmpeg-6.1.6.tar.xz.asc' \
    '397b3becedcd5a98769967ff1ff8501ddc89f8368b8f766e4701377d7dbaabe5  ffmpeg-devel.asc' \
    > SOURCE-SHA256SUMS
sha256sum --check SOURCE-SHA256SUMS
keyring=$(mktemp -d)
trap 'rm -r "$keyring"' EXIT
actual=$(gpg --homedir "$keyring" --batch --with-colons --show-keys ffmpeg-devel.asc |
    awk -F: '$1 == "fpr" { print $10; exit }')
test "$actual" = "$fingerprint"
gpg --homedir "$keyring" --batch --import ffmpeg-devel.asc
gpg --homedir "$keyring" --batch --no-auto-key-retrieve --no-auto-key-import \
    --status-fd 1 --verify "ffmpeg-$version.tar.xz.asc" "ffmpeg-$version.tar.xz" \
    > source-signature-status.txt
grep -Eq "^\[GNUPG:\] VALIDSIG $fingerprint " source-signature-status.txt

mkdir -p /tmp/openjibo-ffmpeg-build
tar -xJf "ffmpeg-$version.tar.xz" -C /tmp/openjibo-ffmpeg-build
cp /tmp/build-ffmpeg-audio.sh "$evidence/build-ffmpeg-audio.sh"
cd "/tmp/openjibo-ffmpeg-build/ffmpeg-$version"
./configure \
    --prefix=/opt/openjibo-ffmpeg \
    --disable-autodetect --disable-everything --disable-network \
    --disable-gpl --disable-version3 --disable-nonfree \
    --disable-static --enable-shared --disable-doc --disable-debug \
    --disable-avdevice --disable-swscale --disable-postproc \
    --disable-ffplay --disable-ffprobe --enable-ffmpeg \
    --enable-protocol=file,pipe \
    --enable-demuxer=ogg,flac,wav,pcm_s16le \
    --enable-decoder=opus,flac,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s24be,pcm_s32le,pcm_s32be,pcm_f32le,pcm_f64le,pcm_u8 \
    --enable-parser=opus,flac \
    --enable-encoder=pcm_s16le --enable-muxer=wav,pcm_s16le \
    --enable-filter=abuffer,abuffersink,anull,aformat,aresample,atrim,asetpts,silenceremove,volume \
    --extra-ldflags=-Wl,-rpath,/opt/openjibo-ffmpeg/lib
cp ffbuild/config.mak "$evidence/config.mak"
cp config.h "$evidence/config.h"
cp COPYING.LGPLv2.1 LICENSE.md "$evidence/"
make -j"$jobs"
make install
/opt/openjibo-ffmpeg/bin/ffmpeg -buildconf > "$evidence/buildconf.txt" 2>&1
/opt/openjibo-ffmpeg/bin/ffmpeg -L > "$evidence/license.txt" 2>&1
