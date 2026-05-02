#!/usr/bin/env bash
set -euo pipefail

storage_root="${1:-}"
port="${MELOTTS_PORT:-5057}"
default_voice_id="${MELOTTS_DEFAULT_VOICE_ID:-zh_female_default}"
preferred_device="${MELOTTS_PREFERRED_DEVICE:-gpu-auto}"

if [[ -z "${storage_root}" ]]; then
  echo "Usage: ./start.sh <storage-root>" >&2
  exit 1
fi

storage_root="$(cd "${storage_root}" 2>/dev/null && pwd || true)"
if [[ -z "${storage_root}" ]]; then
  mkdir -p "$1"
  storage_root="$(cd "$1" && pwd)"
fi

mkdir -p \
  "${storage_root}/cache/huggingface" \
  "${storage_root}/cache/pip" \
  "${storage_root}/cache/torch" \
  "${storage_root}/cache/transformers" \
  "${storage_root}/logs" \
  "${storage_root}/models" \
  "${storage_root}/service" \
  "${storage_root}/temp" \
  "${storage_root}/venv"

export HF_HOME="${storage_root}/cache/huggingface"
export TRANSFORMERS_CACHE="${storage_root}/cache/transformers"
export TORCH_HOME="${storage_root}/cache/torch"
export TEMP="${storage_root}/temp"
export TMP="${storage_root}/temp"
export PIP_CACHE_DIR="${storage_root}/cache/pip"
export MELOTTS_DEFAULT_VOICE_ID="${default_voice_id}"
export MELOTTS_PREFERRED_DEVICE="${preferred_device}"
export MELOTTS_HOST="127.0.0.1"
export MELOTTS_PORT="${port}"
export MELOTTS_STORAGE_ROOT="${storage_root}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${script_dir}"
python -m uvicorn app:app --host 127.0.0.1 --port "${port}"
