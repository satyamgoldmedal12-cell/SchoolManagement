# School Management

.NET 10 MVC + Razor, React, Dapper, SQL Server, Bootstrap, Tailwind, and ASP.NET Core Identity.
See [ARCHITECTURE.md](ARCHITECTURE.md) for the folder guide and request flow.

User database operations are in `Repositories/UserRepository.cs`, behind `IUserRepository`.
`UserService` handles registration, resend and validation; the repository handles user records.
`SqlSession` supplies the shared SQL connection and transaction. Dapper Identity stores persist
users, roles, claims, logins and tokens through stored procedures. Password handling remains
with ASP.NET Identity. Entity Framework Core and ApplicationDbContext have been removed.

Application reads and writes (users/designations, login history, last-login time and administrator
setup queries) use Dapper stored-procedure calls. SQL scripts provide schema initialization.
Identity still handles passwords, normalization, lockout, security stamps and reset tokens.

## Solution

```text
SchoolManagement.csproj                 MVC Web host
Controllers/ Models/ Views/             HTTP and Razor presentation
Interfaces/                             One interface per file
Services/                               Login, registration, email, master-data logic
Repositories/                           Database operations
Data/                                   SQL connection factory, shared transaction and schema setup
Configuration/                          Dependency injection and authentication setup
ClientApp/                              React + Vite + prefixed Tailwind sources
wwwroot/dist/                           Built frontend assets
Database/                               SQL setup and additive upgrades
tests/SchoolManagement.Tests/           SQL-backed integration tests
```

## Registration and first login

Administrators create roles and designations, then register users at `/Users/Create`.
The designation determines the user's role. Public self-registration remains disabled.

Registration emails the user's email address, login URL and a randomly generated temporary password.
It expires after 24 hours. On first login the user **must change this password** before accessing
the application. The first-login session is limited to the password-change page for 10 minutes;
it cannot access protected pages or APIs. The private password must differ from the temporary
password and contain 8–100 characters with uppercase, lowercase, a digit and a symbol.

Temporary credentials are hashed in the database. They are not shown on admin pages, logged,
written to files, or returned from the users API. An email failure retains the account; the admin
can use **Resend login email**, which generates new credentials and invalidates the previous ones.
For users who have already completed setup, **Send reset link** emails the normal recovery link.

Forgot-password recovery remains available at `/Account/ForgotPassword`. Links expire after one hour
and become invalid after use. Password changes invalidate older sessions. Five failed logins lock an
account for 15 minutes; password-help POSTs are limited to 10 requests per source IP per 15 minutes.
Inactive users/designations are blocked, including already signed-in users.

## Database

Before starting this version against an existing database, execute the complete
[Database/20261103_LoginGeoLocation.sql](Database/20261103_LoginGeoLocation.sql),
[Database/20261103_StoredProcedures.sql](Database/20261103_StoredProcedures.sql), then
[Database/20261103_DapperIdentity.sql](Database/20261103_DapperIdentity.sql) in SSMS and restart
the app. The last script adds 25 Identity procedures; it preserves existing users and password hashes.
Procedure installation is manual; the app does not apply scripts on startup.
See [installation steps](Database/StoredProcedures.md).

Database: **SchoolManagement**. Existing table names remain `tblUsers`, `tblRoles`, `tblDesignations`,
plus Identity membership/claim/login/token tables. `tblDesignations.Roleid` and audit user IDs use
`NVARCHAR(450)` to match the Identity keys. Rowversion protects concurrent account-status changes.

Configure a Windows-authenticated SQL Server connection locally:

```powershell
dotnet user-secrets set "ConnectionStrings:SchoolManagement" "Server=(localdb)\MSSQLLocalDB;Database=SchoolManagement;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
```

If LocalDB discovery fails but the SQL process is running, configure its current named-pipe address
in the same secret. The pipe can change after a SQL restart. No machine-specific pipe is committed.

For a fresh database:

```powershell
dotnet run --launch-profile https -- --initialize-database
```

`Database/SchoolManagement.sql` creates or upgrades the original supplied schema, including these
temporary-password columns, login location fields and all 45 procedures. It keeps rows/hashes and rolls back the base upgrade if duplicate emails or invalid legacy
role references exist. Legacy designation Roleid values must match tblRoles.Id; permissions are not guessed.
The old SQLite database, if present in App_Data, is not used or deleted.

## SMTP

Real email requires SMTP configuration. For development use user-secrets; production should use
environment variables or a secret store. Never commit a real SMTP password.

```powershell
dotnet user-secrets set "Smtp:Host" "smtp.your-provider.example"
dotnet user-secrets set "Smtp:Port" "587"
dotnet user-secrets set "Smtp:UseSsl" "false"
dotnet user-secrets set "Smtp:UserName" "sender@your-school.example"
dotnet user-secrets set "Smtp:Password" "YOUR_PROVIDER_APP_PASSWORD"
dotnet user-secrets set "Smtp:FromEmail" "sender@your-school.example"
dotnet user-secrets set "Application:PublicBaseUrl" "https://your-school.example"
```

Port 587 uses required STARTTLS; `UseSsl=true` selects TLS on connect, normally on port 465.
This adapter uses SMTP username/password; OAuth-only providers need an OAuth-capable mail adapter.
Development's default public URL is `https://localhost:7028`, usable only on the same computer.
Use the real HTTPS application address for other users. Email URLs use this configured address,
never an incoming Host header. Missing SMTP settings cause a retryable delivery warning.

For a new installation, create the first administrator locally after database/SMTP setup:

```powershell
dotnet run --launch-profile https -- --bootstrap-admin admin@your-school.example
```

It creates Administrator/Staff roles, the Administrator designation and a pending admin user, then
emails temporary credentials. It refuses a second bootstrap when an administrator already exists.
To retry email after a configuration failure:

```powershell
dotnet run --launch-profile https -- --resend-invitation admin@your-school.example
```

## Build and run

Node.js 24+ and .NET SDK 10 are used for development.

```powershell
cd ClientApp
npm ci
npm run build
cd ..
dotnet restore
dotnet build
dotnet run --launch-profile https
```

Open https://localhost:7028. For frontend development run `npm run dev` in ClientApp; it watches and
rebuilds assets served by Razor. Rebuild the frontend before .NET build/publish after JS/CSS edits.
Use `dotnet publish -c Release` after the frontend build. Built bundles are committed intentionally.

Production requires HTTPS, `ConnectionStrings__SchoolManagement`, `Application__PublicBaseUrl` and
SMTP settings. Keep ignored App_Data/keys and the process account persistent; Windows uses DPAPI
for cookie-encryption keys. The core project contains no default production credentials.

## Tests

```powershell
sqllocaldb create SchoolManagementTests15 15.0 -s
dotnet test tests/SchoolManagement.Tests/SchoolManagement.Tests.csproj
```

Override `SCHOOLMANAGEMENT_TEST_SQL_SERVER` to use another test SQL instance. Tests create and remove
only randomly named `SchoolManagementTests_*` databases and guard against using application data.
They capture email in memory and send no real mail. Coverage includes temporary login and mandatory
change, API authorization, stale setup sessions, expired credentials, resend/email failure, recovery
and session invalidation, Dapper search/injection handling, optimistic concurrency, and legacy schema upgrades.

## Login/logout history

For the latest readable-address fields, execute **Database/20261103_LoginGeoLocation.sql** in full,
configure `Geocoding:ApiKey` in user-secrets and restart the app. This complete upgrade includes the
older area columns and supersedes that deployment step. See [GeoLocation setup](Database/GeoLocation.md).
New permitted logins store GeoLocation, DeviceFormFactor, LocationResponse and lookup status/provider.
Google Maps reverse geocoding supplies formatted addresses; an unavailable key/API retains a clearly
marked offline estimate without blocking login. API keys are never stored in response JSON or shown
to the browser. The administrator grid has a dedicated Address column and expandable session details.

For area-name storage, execute the complete [Database/20261103_LoginArea.sql](Database/20261103_LoginArea.sql)
in SSMS against the existing SchoolManagement database, then restart the app. It adds six nullable columns
and updates the two history procedures. The application does not apply it automatically.

New permitted browser locations also store the nearest available GeoNames place, region, country,
point ID and distance (within 25 km). The UI labels this approximate result **Near**; it is not an exact
address or a verified administrative area. The 4.3 MB offline dataset needs no API key and makes no
external location request. No permission, no nearby point or a missing dataset leaves the area unavailable
without blocking login. Existing history is retained without guessed backfills. See [data provenance](Data/Locations/README.md).

Apply Database/20261031_LoginHistory.sql to an existing database before restarting this version.
The Administrator menu includes Login History (/LoginHistory), with one row per completed browser login.
The table tracks UTC login, explicit logout, last activity, expiry and session end reasons.
Expired sessions are marked every minute while the app runs. Browser closing is not an explicit logout.
Existing cookies require a fresh login. See Database/LoginHistory.md for the table structure and SQL query.

For system/location tracking, also apply Database/20261031_LoginDeviceLocation.sql to an existing history table.
New logins record a protected browser-device ID, OS/browser/device type, IP and browser time zone.
Login and first-password setup request browser location automatically when the page opens; there is no
system-name field or location-sharing button. The browser owns the permission popup and may remember an
earlier Allow/Block choice. HTTPS (or a trusted localhost context) is required. Only permitted coordinates
and accuracy are included. If login is submitted while location is pending, it waits at most eight seconds;
denial, timeout and unavailable location still allow login. Late callbacks cannot change a submitted result.
The history page shows captured details and an optional map link. Existing history is preserved.

