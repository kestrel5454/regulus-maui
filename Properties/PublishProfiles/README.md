# ARM64 軽量 APK

Android SDK と JDK 21 を設定した環境で実行します。

```powershell
dotnet publish RegulusMobile.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android -c Release -r android-arm64 -p:PublishProfile=AndroidSmall
```

出力先: `bin/Release/net10.0-android/android-arm64/publish/com.companyname.regulusmobile-Signed.apk`

- ARM64 の Android 端末向けです。x64 エミュレーター用は含めません。
- フレームワークを partial trimming し、デバッグシンボルを除きます。
- AOT を無効にして容量を削減しています。AOT 版と比べて起動・初回実行が遅くなる場合があります。
- 署名設定は変更しません。この開発環境では従来と同じデバッグ鍵で署名されます。
