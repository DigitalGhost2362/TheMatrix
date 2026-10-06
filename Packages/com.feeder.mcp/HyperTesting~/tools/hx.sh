#!/bin/bash
# usage: hx.sh <hcN|port> file.cs
# runs the file as a method body on that clone (script-execute, body mode); one round trip takes several seconds
export PYTHONIOENCODING=utf-8
exec python "$(cd "$(dirname "$0")" && pwd)/hc.py" "$1" --body "$2"
