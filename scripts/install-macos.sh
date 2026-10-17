#!/usr/bin/env bash
# macOS 版デーモンを発行し、ログイン中は常駐する LaunchAgent として登録する。
# 管理者権限は不要（ユーザー領域に配置し、ユーザーの GUI セッションで動かす）。
# GUI セッションで動かすのは、DDC（IOAVService）がログイン中のセッションからしか読めないため。
# .app バンドルにするのは、macOS のローカルネットワーク権限が素の実行ファイルには付与されず、
# launchd から起動すると TV への接続が "No route to host" で遮断されるため。
set -euo pipefail

LABEL="com.parin1213.lgtv-switcher"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/src/LgtvSwitcher.MacOS/LgtvSwitcher.MacOS.csproj"
APP_DIR="$HOME/Library/Application Support/LgtvSwitcher/LgtvSwitcher.app"
INSTALL_DIR="$APP_DIR/Contents/MacOS"
LOG_DIR="$HOME/Library/Logs/LgtvSwitcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

if ! command -v m1ddc >/dev/null 2>&1 && [ ! -x /opt/homebrew/bin/m1ddc ]; then
  echo "m1ddc が見つかりません。先に 'brew install m1ddc' を実行してください。" >&2
  exit 1
fi

echo "dotnet publish 実行中..."
PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT
dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained -o "$PUBLISH_DIR"

# 稼働中なら止めてから差し替える。
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true

# state.json（ペアリング済みの client-key）はバンドルの外に置かれるため、バンドルだけを入れ替える。
rm -rf "$APP_DIR"
mkdir -p "$INSTALL_DIR" "$LOG_DIR" "$(dirname "$PLIST")"
cp -R "$PUBLISH_DIR/." "$INSTALL_DIR/"

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>$LABEL</string>
  <key>CFBundleName</key>
  <string>LgtvSwitcher</string>
  <key>CFBundleExecutable</key>
  <string>LgtvSwitcher.MacOS</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSLocalNetworkUsageDescription</key>
  <string>LG TV の入力を切り替えるため、同じネットワーク上の TV に接続します。</string>
</dict>
</plist>
EOF

# ローカルネットワーク権限はコード署名に紐づくため、バンドル全体をアドホック署名する。
codesign --force --deep --sign - "$APP_DIR"

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>$LABEL</string>
  <key>AssociatedBundleIdentifiers</key>
  <string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>$INSTALL_DIR/LgtvSwitcher.MacOS</string>
    <string>run</string>
  </array>
  <key>WorkingDirectory</key>
  <string>$INSTALL_DIR</string>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
  <key>ThrottleInterval</key>
  <integer>30</integer>
  <key>ProcessType</key>
  <string>Background</string>
  <key>StandardOutPath</key>
  <string>$LOG_DIR/stdout.log</string>
  <key>StandardErrorPath</key>
  <string>$LOG_DIR/stderr.log</string>
</dict>
</plist>
EOF

launchctl bootstrap "gui/$(id -u)" "$PLIST"
echo "登録しました: $LABEL"
echo "状態確認: launchctl print gui/$(id -u)/$LABEL | grep state"
echo "ログ: $LOG_DIR"
