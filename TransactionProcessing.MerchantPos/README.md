# Merchant POS with Dashboard and Persisted Configuration

This app now provides:
- A single-page operations console at `/`
- Live merchant stats from `/api/dashboard`
- Editable configuration stored in SQLite outside the binaries folder
- Health endpoint at `/health`
- DbContext & repository abstraction `IEfRepository` / `EfRepository`

How to run:
- `dotnet restore`
- `dotnet build`
- `dotnet run`
- Visit `http://localhost:9600/` for the console

Notes:
- `ConnectionStrings:SettingsDb` and `ConnectionStrings:MerchantDb` store plain file paths.
- The code behind adds the SQLite `Data Source=` prefix when opening each database.
- Both paths should point outside the binaries folder, for example under `G:\Git\TransactionProcessing\SupportTools\MerchantPosData\`.
