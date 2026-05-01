#!/bin/bash
# ============================================
# WebCodeCli Docker 启动脚本
# 动态生成 Codex 配置文件
# ============================================

set -e

echo "============================================"
echo "WebCodeCli Docker Container Starting..."
echo "============================================"

# ============================================
# 生成 Codex 配置文件
# ============================================
# 提前设置 HOME 为 appuser 的家目录，确保配置文件创建在正确的位置
export HOME="/home/appuser"
export USER="appuser"

echo "Generating Codex configuration..."

CODEX_CONFIG_DIR="$HOME/.codex"
CODEX_CONFIG_FILE="${CODEX_CONFIG_DIR}/config.toml"

# 确保配置目录存在并属于 appuser
mkdir -p "${CODEX_CONFIG_DIR}"
chown -R appuser:appuser "${CODEX_CONFIG_DIR}"

# 使用环境变量生成配置文件
cat > "${CODEX_CONFIG_FILE}" << EOF
# Codex CLI 配置文件（由 Docker 启动脚本自动生成）
# 生成时间: $(date)

model = "${CODEX_MODEL:-gpt-5.4}"
model_provider = "${CODEX_MODEL_PROVIDER:-${CODEX_PROVIDER_NAME:-meteor-ai}}"
disable_response_storage = true
max_context = ${CODEX_MAX_CONTEXT:-1000000}
context_compact_limit = ${CODEX_CONTEXT_COMPACT_LIMIT:-800000}
approval_policy = "${CODEX_APPROVAL_POLICY:-never}"
sandbox_mode = "${CODEX_SANDBOX_MODE:-danger-full-access}"

rmcp_client = true
model_reasoning_effort = "${CODEX_MODEL_REASONING_EFFORT:-xhigh}"
model_reasoning_summary = "${CODEX_MODEL_REASONING_SUMMARY:-detailed}"
model_verbosity = "${CODEX_MODEL_VERBOSITY:-high}"
model_supports_reasoning_summaries = true

[mcp_servers.claude]
type = "stdio"
command = "claude"
args = ["mcp", "serve"]

[model_providers."${CODEX_MODEL_PROVIDER:-${CODEX_PROVIDER_NAME:-meteor-ai}}"]
name = "${CODEX_PROVIDER_NAME:-meteor-ai}"
base_url = "${CODEX_BASE_URL:-https://api.routin.ai/v1}"
requires_openai_auth = true
wire_api = "${CODEX_WIRE_API:-responses}"

[windows]
sandbox = "elevated"
EOF

echo "Codex configuration generated at: ${CODEX_CONFIG_FILE}"
cat "${CODEX_CONFIG_FILE}"
echo ""

# ============================================
# 验证环境变量
# ============================================
echo "Validating environment variables..."

# Claude Code 配置检查
if [ -z "${ANTHROPIC_AUTH_TOKEN}" ]; then
    echo "WARNING: ANTHROPIC_AUTH_TOKEN is not set. Claude Code may not work properly."
fi

if [ -n "${ANTHROPIC_BASE_URL}" ]; then
    echo "Claude Code Base URL: ${ANTHROPIC_BASE_URL}"
fi

if [ -n "${ANTHROPIC_MODEL}" ]; then
    echo "Claude Code Model: ${ANTHROPIC_MODEL}"
fi

# Codex 配置检查
if [ -z "${NEW_API_KEY}" ]; then
    echo "WARNING: NEW_API_KEY is not set. Codex may not work properly."
fi

echo ""

# ============================================
# 验证 CLI 工具
# ============================================
echo "Verifying CLI tools..."

echo -n "Claude CLI: "
claude --version 2>/dev/null || echo "Not available or version check failed"

echo -n "Codex CLI: "
codex --version 2>/dev/null || echo "Not available or version check failed"

echo -n "Node.js: "
node --version

echo -n "Python: "
python3 --version

echo -n "Git: "
git --version

echo ""

# ============================================
# 创建必要的目录
# ============================================
echo "Creating required directories..."
mkdir -p /app/data
mkdir -p /app/workspaces
mkdir -p /app/logs

# 修复目录权限（entrypoint 以 root 运行，可以修改挂载卷权限）
echo "Fixing directory permissions for mounted volumes..."
chown -R appuser:appuser /app/data /app/workspaces /app/logs /app 2>/dev/null || echo "Note: Some directories could not be chown'd"

# 修复 home 目录权限
if [ -d "/home/appuser" ]; then
    chown -R appuser:appuser /home/appuser 2>/dev/null || true
fi

# ============================================
# 配置 Claude Code Skills
# ============================================
echo "Configuring Claude Code Skills..."
CLAUDE_SKILLS_DIR="/home/appuser/.claude/skills"
if [ -d "/app/skills/claude" ]; then
    echo "Copying Claude Code skills to $CLAUDE_SKILLS_DIR..."
    mkdir -p "$CLAUDE_SKILLS_DIR"
    chown -R appuser:appuser /home/appuser/.claude
    cp -r /app/skills/claude/* "$CLAUDE_SKILLS_DIR/"
    chown -R appuser:appuser /home/appuser/.claude
    echo "Claude Code skills installed:"
    ls "$CLAUDE_SKILLS_DIR" || echo "No skills found"
fi

# ============================================
# 配置 Codex Skills
# ============================================
echo "Configuring Codex Skills..."
CODEX_SKILLS_DIR="/home/appuser/.codex/skills"
if [ -d "/app/skills/codex" ]; then
    echo "Copying Codex skills to $CODEX_SKILLS_DIR..."
    mkdir -p "$CODEX_SKILLS_DIR"
    chown -R appuser:appuser /home/appuser/.codex
    cp -r /app/skills/codex/* "$CODEX_SKILLS_DIR/"
    chown -R appuser:appuser /home/appuser/.codex
    echo "Codex skills installed:"
    ls "$CODEX_SKILLS_DIR" || echo "No skills found"
fi

echo ""

# ============================================
# 启动应用（切换到 appuser 用户运行）
# ============================================
echo "Starting WebCodeCli application as appuser..."
echo "============================================"

# 使用 setpriv 切换到 appuser 用户执行命令
# 注意：HOME 环境变量已在脚本开头设置
exec setpriv --reuid=appuser --regid=appuser --clear-groups "$@"
