#!/usr/bin/env bash
# macOS 版デーモンの LaunchAgent を解除し、発行物を削除する。
# state.json（ペアリング済みの client-key）は再インストール時にペアリングをやり直さずに済むよう残す。
set -euo pipefail

LABEL="com.parin1213.lgtv-switcher"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
rm -f "$PLIST"
rm -rf "$HOME/Library/Application Support/LgtvSwitcher/LgtvSwitcher.app"
echo "解除しました: $LABEL"
