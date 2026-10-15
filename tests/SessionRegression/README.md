Run the lifecycle/session regression checks without MAUI or a device:

```powershell
dotnet run --project tests/SessionRegression
node tests/SessionRegression/document-session.test.cjs
```

The C# runner links the production session partial and substitutes only UI/network
effects and time. The JavaScript checks use the production script exported by it.

Device verification (saved J-system credentials required):

1. Sleep or background the app for ten minutes, then return. Re-login starts once;
   opening customer details while it runs should complete without a preliminary error.
2. Expire the server session during a shorter sleep. Open current and historical
   customer details and search; login redirects and login HTML returned at the original
   URL should both recover and resume the original operation.
3. Sleep while a read or login is running. Return and confirm it resumes without an
   old timeout failing it. Also check unavailable network and incorrect credentials:
   genuine failures should terminate, without an endless loading state.
