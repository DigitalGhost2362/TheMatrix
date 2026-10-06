#!/bin/bash
# usage: hxf.sh <hcN|port> file.cs
# full-code mode: the file defines class HT with static void Main(); use it for loops that sample state inside the game
export PYTHONIOENCODING=utf-8
exec python "$(cd "$(dirname "$0")" && pwd)/hc.py" "$1" --class "$2"
