#!/bin/sh
# Builds GlassDesk.exe (and optionally the MSI) on Linux with Mono + msitools.
#   sudo apt install mono-devel wixl
#   ./build.sh          -> build/GlassDesk.exe
#   ./build.sh msi      -> also build/GlassDesk-Setup.msi
# It compiles against the real .NET Framework 4.5 reference assemblies, so only APIs that
# exist on Windows can be used.
set -e
cd "$(dirname "$0")"
REF=/usr/lib/mono/4.5-api
mkdir -p build
mcs -langversion:5 -target:winexe -optimize+ -noconfig -nostdlib -sdk:4.5 \
  -lib:$REF -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll \
  -win32icon:assets/GlassDesk.ico -out:build/GlassDesk.exe src/*.cs
cp assets/GlassDesk.exe.config build/
echo "Built build/GlassDesk.exe"
if [ "$1" = "msi" ]; then
  (cd installer && wixl -o ../build/GlassDesk-Setup.msi GlassDesk.wxs)
  echo "Built build/GlassDesk-Setup.msi"
fi
