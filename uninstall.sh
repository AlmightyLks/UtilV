#!/usr/bin/env bash
#
# Removes what install.sh created. Settings in ~/.config/utilv are left alone.

set -euo pipefail

LIB="${HOME}/.local/lib/utilv"
BIN="${HOME}/.local/bin/utilv"
UNIT="${HOME}/.config/systemd/user/utilv.service"

# Every step is optional: a partial install must still uninstall cleanly.
systemctl --user disable --now utilv.service 2>/dev/null || true
rm -f "${UNIT}"
systemctl --user daemon-reload 2>/dev/null || true

rm -rf "${LIB:?}"
rm -f "${BIN}"

echo "UtilV removed. Settings kept in ${HOME}/.config/utilv"
echo "The keyboard shortcut is built in, so there is nothing to unbind."
