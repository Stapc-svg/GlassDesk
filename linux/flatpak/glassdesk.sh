#!/bin/sh
# Starts GlassDesk inside Flatpak.
export PYTHONPATH=/app/lib/glassdesk${PYTHONPATH:+:$PYTHONPATH}
exec python3 -m glassdesk "$@"
