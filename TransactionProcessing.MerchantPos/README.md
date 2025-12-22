# Merchant POS with EF Core and Health Dashboard

This skeleton demonstrates:
- EF Core (SQLite) persistent store (`merchantpos.db`)
- Minimal health endpoint at `/health`
- Dashboard endpoint at `/dashboard` (returns balances JSON)
- DbContext & repository abstraction `IEfRepository` / `EfRepository`

How to run:
- `dotnet restore`
- `dotnet build`
- `dotnet run`
- Visit `http://localhost:5000/health` and `/dashboard`

Notes:
- This is a skeleton. The worker loop is a placeholder — replace with your merchant runtime logic.
- Database file will be created next to the application: `merchantpos.db`.
