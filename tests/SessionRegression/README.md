Run the lifecycle/session regression checks without MAUI or a device:

```powershell
dotnet run --project tests/SessionRegression
node tests/SessionRegression/document-session.test.cjs
node tests/SessionRegression/emergency-bridge.test.cjs
```

The C# runner links the production session partial and substitutes only UI/network
effects and time. The JavaScript checks use the production script exported by it.
Emergency checks also use the production comment parser and bridge helpers, with
fictional data matching the supplied October 24 comment structure.

Emergency device verification (use only October 24, Matsui's first card):

1. Enable 「緊急措置設定」 in Settings, return to REGULUS, and open that card.
   Check the second comment's ID, customer/contact/store information, request,
   promise and supplements. If the comment contains 料金明細 / 明細情報 / 明細,
   check that every detail row and total appears in 明細情報. No J-system detail
   read is expected. Comments without details must not show an empty detail section.
2. Disable the setting and reopen the same card. The original J-system retrieval
   and charge section must return. Restarting the app must preserve the setting.

Device verification (saved J-system credentials required):

1. Sleep or background the app for ten minutes, then return. Re-login starts once;
   opening customer details while it runs should complete without a preliminary error.
2. Expire the server session during a shorter sleep. Open current and historical
   customer details and search; login redirects and login HTML returned at the original
   URL should both recover and resume the original operation.
3. Sleep while a read or login is running. Return and confirm it resumes without an
   old timeout failing it. Also check unavailable network and incorrect credentials:
   genuine failures should terminate, without an endless loading state.
