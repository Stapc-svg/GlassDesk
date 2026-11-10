#!/bin/sh
# Compile against the real .NET Framework 4.5 reference assemblies (same APIs Windows has),
# so nothing Mono-only can sneak into the .exe.
REF=/usr/lib/mono/4.5-api
mcs -langversion:5 -target:winexe -optimize+ -noconfig -nostdlib -sdk:4.5 \
  -lib:$REF -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll \
  -win32icon:assets/GlassDesk.ico -resource:assets/logo.png,GlassDesk.logo.png -out:build/GlassDesk.exe src/*.cs
