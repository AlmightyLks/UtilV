#!/usr/bin/env bash
#
# Installs UtilV under ~/.local and starts it as a systemd user service.

set -euo pipefail

LIB="${HOME}/.local/lib/utilv"
BIN="${HOME}/.local/bin"
UNIT="${HOME}/.config/systemd/user/utilv.service"

SOURCE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# In a release there is a binary beside this script; in the source tree there is not.
if [[ ! -x "${SOURCE}/utilv" ]]; then
    PROJECT="${SOURCE}/UtilV/UtilV.csproj"
    SOURCE="$(mktemp -d)"
    trap 'rm -rf "${SOURCE}"' EXIT

    echo "Building..."
    dotnet publish "${PROJECT}" -c Release -r linux-x64 -o "${SOURCE}" --nologo -v quiet
fi

# The binary loads its libraries from its own directory, so everything is installed
# together and only a symlink goes on PATH.
echo "Installing to ${LIB}..."
install -D -m 755 "${SOURCE}/utilv" "${LIB}/utilv"
install -m 644 "${SOURCE}"/*.so "${LIB}/"
install -m 755 "${SOURCE}/uninstall.sh" "${LIB}/uninstall.sh"
mkdir -p "${BIN}"
ln -sfn "${LIB}/utilv" "${BIN}/utilv"

mkdir -p "$(dirname "${UNIT}")"
cat > "${UNIT}" <<EOF
[Unit]
Description=UtilV clipboard history
PartOf=graphical-session.target
After=graphical-session.target

[Service]
Type=simple
# Keeps UtilV out of the desktop's X session management, which otherwise waits at
# shutdown for a client that has no session state to save.
Environment=SESSION_MANAGER=
ExecStart=${LIB}/utilv
Restart=on-failure
RestartSec=2

[Install]
WantedBy=graphical-session.target
EOF

# DISPLAY and XAUTHORITY change every login, so they are imported rather than hardcoded.
systemctl --user import-environment DISPLAY XAUTHORITY 2>/dev/null || true

systemctl --user daemon-reload
systemctl --user enable utilv.service
systemctl --user restart utilv.service

echo
echo "Done. Press Alt+V to open the clipboard history."
echo "Uninstall with ${LIB}/uninstall.sh"
