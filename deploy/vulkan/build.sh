#!/usr/bin/env bash
#
# Builds the two GPU services MovieBot's voice surface runs on — llama.cpp's server and
# whisper.cpp's library — and assembles each into a self-contained tree that installs to
# /opt/moviebot on hotbox.
#
# This runs on hotrod. hotbox carries no compiler, and the two machines are the same Arch
# release with the same glibc and libstdc++, so a binary built here runs there.
#
# Run: ./build.sh [--clean]

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="${SRC_ROOT:-$HOME/src}"
STAGE="${STAGE:-$HERE/stage}"
JOBS="${JOBS:-$(( $(nproc) / 2 ))}"

# Pinned, because "whatever master was that day" is not a thing anyone can rebuild.
#
# whisper.cpp is pinned to the release Whisper.net 1.9.1 binds against. The speech daemon
# reaches whisper through Whisper.net's P/Invoke rather than through whisper-server, so the
# library ABI is the contract and a newer whisper is a worse match, not a better one.
WHISPER_REF="v1.9.1"
LLAMA_REF="97e4ca7"

# hotbox is an Athlon X4 860K: AVX, FMA and F16C, and NO AVX2. hotrod's Ryzen has AVX2, so a
# default GGML_NATIVE=ON build here emits instructions that machine cannot execute and dies
# with SIGILL on the first matrix multiply. These flags are the whole reason this script
# exists rather than a pacman install.
CPU_FLAGS=(
  -DGGML_NATIVE=OFF
  -DGGML_AVX=ON
  -DGGML_AVX2=OFF
  -DGGML_FMA=ON
  -DGGML_F16C=ON
  -DGGML_AVX512=OFF
  -DGGML_BMI2=OFF
)

# GGML_BACKEND_DL is OFF, which is the default from source and the opposite of what Arch
# ships. With it ON the Vulkan backend is dlopen'd from a directory compiled into libggml —
# /usr/lib/ggml, which needs root to populate — and a backend that is not found is not an
# error: the program starts, reports no devices, and runs every token on the CPU. Linked
# through DT_NEEDED instead, it either resolves at startup or the process refuses to start.
COMMON=(
  -DCMAKE_BUILD_TYPE=Release
  -DGGML_VULKAN=ON
  -DGGML_BACKEND_DL=OFF
  -DBUILD_SHARED_LIBS=ON
  "${CPU_FLAGS[@]}"
)

log() { printf '\n== %s\n' "$*"; }

if [[ "${1:-}" == "--clean" ]]; then
  rm -rf "$STAGE" "$SRC_ROOT/whisper.cpp/build-vulkan" "$SRC_ROOT/llama.cpp/build-vulkan"
fi

need() { command -v "$1" >/dev/null || { echo "missing build dependency: $1" >&2; exit 1; }; }
need cmake; need git; need glslc; need patchelf
[[ -f /usr/include/spirv/unified1/spirv.hpp ]] || [[ -d /usr/share/cmake/SPIRV-Headers ]] \
  || echo "note: spirv-headers may be missing; the Vulkan backend needs it" >&2

checkout() {  # repo url, directory, ref
  local url="$1" dir="$2" ref="$3"
  if [[ ! -d "$dir/.git" ]]; then
    git clone "$url" "$dir"
  fi
  # Already sitting on it: leave the tree alone. A fetch here is a few hundred megabytes to
  # learn nothing, and re-checking out the same ref would touch files and force a rebuild.
  if [[ "$(git -C "$dir" rev-parse --short HEAD 2>/dev/null)" == "${ref:0:7}" ]] \
     || [[ "$(git -C "$dir" describe --tags --exact-match 2>/dev/null)" == "$ref" ]]; then
    return
  fi
  git -C "$dir" fetch --tags --depth 50 origin "$ref" 2>/dev/null \
    || git -C "$dir" fetch --tags --unshallow origin 2>/dev/null \
    || git -C "$dir" fetch --tags origin
  git -C "$dir" checkout --quiet "$ref"
}

# --- whisper -----------------------------------------------------------------
log "whisper.cpp $WHISPER_REF"
checkout https://github.com/ggml-org/whisper.cpp.git "$SRC_ROOT/whisper.cpp" "$WHISPER_REF"

# WHISPER_COMMON_FFMPEG stays off. Arch turns it on, which puts libavformat and about a
# hundred transitive libraries into the NEEDED chain. The daemon hands whisper 16 kHz mono
# s16le, which is its native input, so there is nothing for ffmpeg to decode.
cmake -S "$SRC_ROOT/whisper.cpp" -B "$SRC_ROOT/whisper.cpp/build-vulkan" \
  "${COMMON[@]}" \
  -DWHISPER_BUILD_TESTS=OFF \
  -DWHISPER_BUILD_EXAMPLES=ON \
  -DWHISPER_COMMON_FFMPEG=OFF
cmake --build "$SRC_ROOT/whisper.cpp/build-vulkan" -j "$JOBS"

# --- llama -------------------------------------------------------------------
log "llama.cpp $LLAMA_REF"
checkout https://github.com/ggml-org/llama.cpp.git "$SRC_ROOT/llama.cpp" "$LLAMA_REF"
cmake -S "$SRC_ROOT/llama.cpp" -B "$SRC_ROOT/llama.cpp/build-vulkan" \
  "${COMMON[@]}" \
  -DLLAMA_BUILD_TESTS=OFF \
  -DLLAMA_BUILD_EXAMPLES=OFF \
  -DLLAMA_BUILD_SERVER=ON \
  -DLLAMA_CURL=ON
cmake --build "$SRC_ROOT/llama.cpp/build-vulkan" -j "$JOBS"

# --- assemble ----------------------------------------------------------------
# Each tree is self-contained: bin/ next to lib/, and every binary carries an RPATH of
# $ORIGIN/../lib. Nothing needs LD_LIBRARY_PATH, nothing is installed outside the tree, and
# a tree can be moved or run by hand without a wrapper.
assemble() {  # name, build dir
  local name="$1"
  local build="$2"
  local dest="$STAGE/$name"
  rm -rf "$dest"; mkdir -p "$dest/bin" "$dest/lib" "$dest/models"

  # Real files only: the build tree is full of versioned symlinks pointing at each other.
  find "$build/bin" -maxdepth 1 -name '*.so*' -type f -exec cp -a {} "$dest/lib/" \;
  find "$build/bin" -maxdepth 1 -name '*.so*' -type l -exec cp -a {} "$dest/lib/" \;

  for exe in "${@:3}"; do
    [[ -f "$build/bin/$exe" ]] || { echo "expected binary missing: $exe" >&2; exit 1; }
    cp -a "$build/bin/$exe" "$dest/bin/"
    patchelf --set-rpath '$ORIGIN/../lib' "$dest/bin/$exe"
  done
  for so in "$dest"/lib/*.so*; do
    if [[ ! -L "$so" ]]; then
      patchelf --set-rpath '$ORIGIN' "$so"
    fi
  done

  printf '%s\n' "$name" > "$dest/BUILD-INFO"
  {
    echo "built:   $(date -Is)"
    echo "host:    $(uname -srm)"
    echo "glibc:   $(ldd --version | head -1)"
    echo "cpu:     AVX FMA F16C, no AVX2 (hotbox: Athlon X4 860K)"
    echo "vulkan:  linked, not dlopen'd (GGML_BACKEND_DL=OFF)"
  } >> "$dest/BUILD-INFO"
}

log "assembling into $STAGE"
assemble whisper "$SRC_ROOT/whisper.cpp/build-vulkan" whisper-cli whisper-server
echo "source: whisper.cpp $WHISPER_REF ($(git -C "$SRC_ROOT/whisper.cpp" rev-parse --short HEAD))" \
  >> "$STAGE/whisper/BUILD-INFO"

assemble llm "$SRC_ROOT/llama.cpp/build-vulkan" llama-server
cp -a "$HERE/warm-llm.sh" "$STAGE/llm/bin/warm-llm.sh"
# The body the warm-up sends. MovieBot.Assistant replaces this with its real prompt
# and catalog; a warm-up of the wrong shape leaves the first real request cold.
cp -a "$HERE/warmup.json" "$STAGE/llm/warmup.json"
echo "source: llama.cpp $LLAMA_REF ($(git -C "$SRC_ROOT/llama.cpp" rev-parse --short HEAD))" \
  >> "$STAGE/llm/BUILD-INFO"

# --- verify ------------------------------------------------------------------
# A binary that resolves every library here but not on hotbox is the failure this catches
# late and expensively, so check the two things that differ between the machines: an
# unresolved NEEDED entry, and an instruction hotbox cannot run.
log "verifying"
fail=0
for exe in "$STAGE"/*/bin/*; do
  if ldd "$exe" 2>/dev/null | grep -q "not found"; then
    echo "UNRESOLVED: $exe"; ldd "$exe" | grep "not found"; fail=1
  fi
done
for so in "$STAGE"/*/lib/*.so*; do
  if [[ -L "$so" ]]; then continue; fi
  n=$(objdump -d "$so" 2>/dev/null | grep -coE '\b(vpermd|vpbroadcastd|vpgatherdd|vinserti128|pdep|pext)\b' || true)
  if [[ "${n:-0}" -gt 0 ]]; then
    echo "AVX2 FOUND in $(basename "$so") ($n) — this will SIGILL on hotbox"; fail=1
  fi
done
[[ $fail -eq 0 ]] && echo "clean: every library resolves, no AVX2 in any shipped object"

du -sh "$STAGE"/*
echo
echo "next: ./fetch-models.sh, then ./install.sh"
exit $fail
