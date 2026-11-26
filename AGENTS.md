# Working on Khodamsaz

## Architecture and scope

- Domain defines entities and listing states. Application owns use-case contracts and shared formatting. Infrastructure implements EF Core/PostgreSQL, Identity, queries, and migrations. Web contains MVC controllers, Razor, JavaScript, and presentation.
- Follow existing conventions. Keep controller authorization, service rules, and database constraints in their respective layers. Avoid unrelated edits and preserve established routes.
- Preserve Persian copy, RTL layout, accessible controls, the two-column mobile grid, and Jalali listing-registration dates in Asia/Tehran.

## Product rules

- Guests see approved listings. Moderation, ownership checks, favorites, and conversation privacy are enforced on the server.
- ASP.NET Core Identity manages accounts and the Admin role. Require anti-forgery on browser writes and verify ownership independently of input. Never commit credentials or publish personal data.
- Purchase declarations are self-reported per product. A user may declare up to ten times per product across all sessions. Preserve PostgreSQL concurrency safeguards and request-ID idempotency. Owners cannot declare their own products. Public views show aggregate counts only.
- Keep absolute canonicals on the public canonical origin. Sitemap entries must be public and indexable. Keep filtered/private pages out of the index and preserve 404s and existing redirects. Structured data must reflect actual values and must never represent self-reports as ratings, reviews, or verified purchases.

## Development and verification

- Use the pinned .NET SDK and PostgreSQL 17. See [README.md](README.md) for setup and the full test list.
- Use disposable test databases, generated local fixture passwords, and development-only demo data. Run Release build and appropriate PostgreSQL, HTTP, SEO, and browser regression suites. Check narrow RTL viewports for UI changes.
- Generate EF migrations in Infrastructure using Web as the startup project. Review generated SQL and test against disposable PostgreSQL and representative existing data. Prefer additive, reversible changes.

## Deployment and preservation

- Treat production SSH and database access as separately configured and explicitly authorized. Use private secret storage and keep access details out of this repository.
- Verify backups of database, uploads, keys, and release configuration before an authorized deployment. Keep the previous app image and preserve all Compose volumes. Avoid destructive migration, reseeding, and volume recreation.
- Verify HTTPS, redirects, startup logs, and user-facing behavior after a release. Document any compatibility limits before rolling back.
