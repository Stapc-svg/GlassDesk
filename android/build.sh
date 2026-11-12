#!/bin/bash
# Builds GlassDesk for Android with Ubuntu's Android tools:
#   sudo apt install aapt dalvik-exchange apksigner zipalign default-jdk
# ANDROID_JAR must point at an android.jar for API 26 or newer (API 33 used here).
set -euo pipefail
cd "$(dirname "$0")"
ANDROID_JAR=${ANDROID_JAR:-sdk/android.jar}
VER=1.7.2
OUT=build
rm -rf $OUT/desk $OUT/gen $OUT/classes $OUT/*.apk $OUT/classes.dex
mkdir -p $OUT/desk $OUT/gen $OUT/classes $OUT/sc

echo "== previews and icon"
SHARED="src/app/freedev/glassdesk/G.java src/app/freedev/glassdesk/Sq.java src/app/freedev/glassdesk/Sym.java src/app/freedev/glassdesk/Data.java src/app/freedev/glassdesk/Style.java src/app/freedev/glassdesk/Draw.java"
javac -nowarn -d $OUT/desk $SHARED desktop/*.java
java -Djava.awt.headless=true -cp $OUT/desk app.freedev.glassdesk.Preview assets res/drawable-nodpi-v4 $OUT/sc
java -Djava.awt.headless=true -cp $OUT/desk app.freedev.glassdesk.IconGen assets res $OUT/sc

echo "== resources"
aapt package -f -m -J $OUT/gen -M AndroidManifest.xml -S res -A assets -I "$ANDROID_JAR" \
     --min-sdk-version 26 --target-sdk-version 34 -F $OUT/res.apk

echo "== java"
javac -nowarn -source 8 -target 8 -encoding UTF-8 -bootclasspath "$ANDROID_JAR" -d $OUT/classes \
      $(find src $OUT/gen -name '*.java')

echo "== dex"
dalvik-exchange --dex --min-sdk-version=26 --output=$OUT/classes.dex $OUT/classes

echo "== package"
cp $OUT/res.apk $OUT/unsigned.apk
(cd $OUT && aapt add unsigned.apk classes.dex >/dev/null)
zipalign -f -p 4 $OUT/unsigned.apk $OUT/aligned.apk
if [ ! -f keystore/glassdesk.jks ]; then
  mkdir -p keystore
  keytool -genkeypair -keystore keystore/glassdesk.jks -alias glassdesk -keyalg RSA -keysize 2048 -validity 10000 \
          -storepass glassdesk -keypass glassdesk -dname "CN=GlassDesk, O=freedev.app" >/dev/null 2>&1
fi
apksigner sign --ks keystore/glassdesk.jks --ks-key-alias glassdesk --ks-pass pass:glassdesk --key-pass pass:glassdesk \
          --min-sdk-version 26 --out $OUT/GlassDesk-Android-$VER.apk $OUT/aligned.apk
apksigner verify --verbose $OUT/GlassDesk-Android-$VER.apk | head -5
ls -la $OUT/GlassDesk-Android-$VER.apk
