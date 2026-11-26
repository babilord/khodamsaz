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
