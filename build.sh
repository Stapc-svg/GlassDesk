#!/bin/sh
# Compile against the real .NET Framework 4.5 reference assemblies (same APIs Windows has),
# so nothing Mono-only can sneak into the .exe.
REF=/usr/lib/mono/4.5-api
# Windows version details, icon and manifest (from the version in Program.cs)
V=$(grep -o 'AssemblyVersion("[0-9.]*' src/Program.cs | grep -o '[0-9][0-9.]*' | cut -d. -f1-3)
mkdir -p build
sed -e "s/VMAJ,VMIN,VPAT/$(echo $V | tr . ,)/g" -e "s/VSTR/$V.0/g" assets/win32/GlassDesk.rc > build/GlassDesk.rc
cp assets/win32/GlassDesk.manifest build/ && sed -i "s/version=\"[0-9.]*\" name=/version=\"$V.0\" name=/" build/GlassDesk.manifest
cp assets/GlassDesk.ico build/ 2>/dev/null; sed -i 's#"../GlassDesk.ico"#"GlassDesk.ico"#' build/GlassDesk.rc
(cd build && llvm-rc -no-preprocess -fo GlassDesk.res GlassDesk.rc) || exit 1
mcs -langversion:5 -target:winexe -optimize+ -noconfig -nostdlib -sdk:4.5 \
  -lib:$REF -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll -r:$REF/System.Management.dll -r:$REF/Microsoft.VisualBasic.dll \
  -win32res:build/GlassDesk.res -resource:assets/logo.png,GlassDesk.logo.png -resource:assets/scenes/morning.jpg,GlassDesk.scene.morning.jpg -resource:assets/scenes/day.jpg,GlassDesk.scene.day.jpg -resource:assets/scenes/sunset.jpg,GlassDesk.scene.sunset.jpg -resource:assets/scenes/night.jpg,GlassDesk.scene.night.jpg $(for f in assets/lang/*.txt; do n=$(basename $f); printf -- "-resource:%s,GlassDesk.lang.%s " "$f" "$n"; done) -out:build/GlassDesk.exe src/*.cs
