# خودم‌ساز · Khodamsaz

A Persian, right-to-left marketplace for handmade products and independent makers. Visitors browse approved listings and contact makers directly. The application provides moderation, favorites, private conversations, and self-reported purchase counts.

## Architecture

| Path | Role |
| --- | --- |
| `src/HandmadeMarketplace.Domain` | Marketplace entities and states |
| `src/HandmadeMarketplace.Application` | Use-case contracts, search models, and Persian formatting |
| `src/HandmadeMarketplace.Infrastructure` | EF Core, PostgreSQL migrations, ASP.NET Core Identity, and persistence |
| `src/HandmadeMarketplace.Web` | ASP.NET Core 9 MVC, Razor views, RTL assets, and SEO endpoints |
| `tests` | .NET regression and HTTP suites, plus Python SEO and Playwright browser checks |

## Local development

Install the .NET 9 SDK version in `global.json` or a later patch in its feature band. Install Docker with Compose v2, or provision PostgreSQL 17 separately. Python 3 and Playwright are needed for browser checks.

1. Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` to a locally generated value. Keep the file local.
2. Run `docker compose up --build -d` and visit http://localhost:5180. The app applies migrations at startup.
3. For isolated demo fixtures, set `ASPNETCORE_ENVIRONMENT=Development`, `SEED_DEMO_DATA=true`, and `SEED_DEMO_PASSWORD` to a password you generate for the test database. Use a fresh, separate project with `docker compose -p khodamsaz-test up --build -d` and a separate `APP_PORT` if needed. The demo accounts are `admin@handmade.local`, `sara@handmade.local`, and `ali@handmade.local`.

For a host-run app, create a PostgreSQL database and a user that can apply schema migrations. Set `ConnectionStrings__Marketplace` to the local connection string, or provide `Database__Host`, `Database__Port`, `Database__Name`, `Database__User`, and `Database__Password`. Then run `dotnet run --project src/HandmadeMarketplace.Web --no-launch-profile --urls http://localhost:5180`. For demo fixtures on the host, set `ASPNETCORE_ENVIRONMENT=Development`, `Seed__DemoData=true`, and `Seed__DemoPassword`.

| Configuration | Purpose |
| --- | --- |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Compose database and its local credentials |
| `APP_PORT`, `APP_BIND_ADDRESS` | Host port and bind address |
| `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD` | Optional one-time admin bootstrap |
| `SEED_DEMO_DATA`, `SEED_DEMO_PASSWORD` | Development-only fixture seeding |
| `DataProtection__KeysPath` | Persistent key directory for a host-run app |

## Build and test

```sh
dotnet restore HandmadeMarketplace.sln
dotnet build HandmadeMarketplace.sln -c Release --no-restore
dotnet run --project tests/HandmadeMarketplace.ImprovementTests
dotnet run --project tests/HandmadeMarketplace.PaginationTests
dotnet run --project tests/HandmadeMarketplace.SmokeTests -- http://localhost:5180
python tests/verify_seo.py http://localhost:5180
python -m pip install playwright
python -m playwright install chromium
python tests/verify_mobile.py http://localhost:5180
python tests/verify_purchase_declarations.py http://localhost:5180
```

Set `IMPROVEMENT_TEST_CONNECTION` and `PAGINATION_TEST_CONNECTION` to a **test-only** PostgreSQL connection with database creation permissions. The .NET PostgreSQL suites create disposable databases. Run the HTTP and browser suites against a fresh, seeded development database. Set `Seed__DemoPassword` in the test runner to the password configured on the app. The smoke and purchase suites write test records. Set `BROWSER_CHANNEL=chromium` to use Playwright Chromium, or install Edge for the scripts’ default channel.

## Contributing and deployment

Start from an up-to-date `main`, create a topic branch, run checks, then open and review a pull request. Read [AGENTS.md](AGENTS.md) for product rules and operational constraints.

Use separately configured deployment access and secrets. Before releasing, verify database, upload, key, and configuration backups. Build a versioned image, replace the app through Compose with the existing volume mappings, verify health and HTTPS, and retain the previous image for rollback. Keep demo seeding disabled outside isolated development. Review migrations before shipping.

## Codex Web

Connect your GitHub account in Codex Web and grant access to this repository. Select khodamsaz and main when creating a cloud environment. Configure a .NET SDK compatible with global.json and run dotnet restore HandmadeMarketplace.sln during setup. Provision disposable PostgreSQL for DB tests and keep development-only values in the environment settings. Run the build and test commands above, review the diff, and open a pull request. After merging, use git switch main and git pull --ff-only on your computers before starting a new topic branch.

## Mobile OTP and MeliPayamak

Email authentication and registration remain available with SMS disabled. SMS login, phone verification, and password recovery are limited to ordinary accounts without MFA or assigned roles. Existing numbers keep their old Identity fields and only become eligible for SMS after an explicit verification. Linking a mobile to an existing account also requires the current password.

| Compose variable | Host configuration | Purpose |
| --- | --- | --- |
| `SMS_ENABLED` | `Sms__Enabled` | Feature switch, default false |
| `SMS_FAKE` | `Sms__Fake` | In-memory SMS sink, Development only and never a live sender |
| `SMS_ENDPOINT` | `Sms__Endpoint` | HTTPS MeliPayamak template REST operation, defaults to BaseServiceNumber |
| `SMS_USERNAME`, `SMS_PASSWORD`, `SMS_API_KEY` | `Sms__Username`, `Sms__Password`, `Sms__ApiKey` | Provider username and credential. The API key, when present, replaces the password in the provider's form field. |
| `SMS_BODY_ID` | `Sms__BodyId` | Approved numeric template identifier. This operation uses the template's service number. |
| `SMS_HASH_KEY` | `Sms__HashKey` | Base64 of 32 or more cryptographically random bytes, identical across app instances |
| `SMS_TRUSTED_PROXY` | `Sms__TrustedProxy` | Exact address of the immediate trusted proxy that supplies the client IP |
| `SMS_TIMEOUT_SECONDS` | `Sms__TimeoutSeconds` | Provider request timeout in seconds, 1–30 |
| `SMS_PHONE_HOURLY`, `SMS_PHONE_DAILY` | `Sms__PhoneHourly`, `Sms__PhoneDaily` | Rolling send-request limits per phone |
| `SMS_IP_HOURLY`, `SMS_IP_DAILY` | `Sms__IpHourly`, `Sms__IpDaily` | Rolling send-request limits per client IP |

Create the template under **ابزار ویژه → وب‌سرویس خدماتی → درج متن جدید**. MeliPayamak's numbering starts at `{0}`, which is the only variable in this exact template. [Provider template instructions](https://www.melipayamak.com/blog/posts/sms-with-pattern-mode/)

```text
به خودم‌ساز خوش آمدید.
کد تأیید شما {0} است.
khodamsaz.ir
```

The [official MeliPayamak .NET REST example](https://github.com/Melipayamak/melipayamak-Asp.net-Core/blob/master/RestClientAsync.cs) documents the form fields `username`, `password`, `text`, `to`, and `bodyId`. For a one-variable template, the application sends the six-digit code alone in `text`. Delivery acceptance requires REST status 1 and a receipt longer than 15 digits. Check the provider’s account requirements for an API key and allowed outbound IP. [MeliPayamak shared service documentation](https://www.melipayamak.com/api/sendbybasenumber/)

After explicit approval for live activation, put the credentials, BodyId, generated hash key, and trusted proxy setting in private runtime configuration. Keep the fake switch off. Verify that the app receives the real client IP from the trusted proxy, enable SMS, and use one authorized test number to check the rendered text, sign-in, and cooldown. Disable SMS again if any of those checks fail.

Local verification uses a disposable PostgreSQL database, a generated hash key, and the Development fake. The PostgreSQL improvement suite exercises the full OTP journey. Start a disposable app with `Sms__Enabled=true`, `Sms__Fake=true`, and a generated `Sms__HashKey` to run `python tests/verify_mobile_otp.py http://localhost:5180`. Do not expose a fake inbox or persist test codes to a shared log.

Before a disabled production rollout, test the additive migration on a restored database and keep the previous app image. Roll back the app image if needed and leave the additive schema in place. Rolling the migration down would discard verified mobile bindings.
