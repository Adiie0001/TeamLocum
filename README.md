# TeamLocum

[![.NET Build & Test](https://github.com/Adiie0001/TeamLocum/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adiie0001/TeamLocum/actions/workflows/dotnet.yml)
[![CodeQL](https://github.com/Adiie0001/TeamLocum/actions/workflows/codeql.yml/badge.svg)](https://github.com/Adiie0001/TeamLocum/actions/workflows/codeql.yml)
![.NET](https://img.shields.io/badge/.NET-10_LTS-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-10_+_SQLite-512BD4?style=flat-square)
![ASP.NET Identity](https://img.shields.io/badge/auth-ASP.NET_Identity_+_roles-2ea44f?style=flat-square)

A locum-staffing web application built with ASP.NET Core MVC. Hospitals and clinics post shifts, doctors apply for
them, and the hospital assigns one doctor. An administrator approves every new hospital and doctor before they can act.
The system guarantees that a doctor never holds two overlapping shifts, ranks candidates with a transparent scoring
rule (and can optionally ask Google Gemini, on anonymised data only), and keeps each role's view of the data separate.

Everything stated here is something a test, the CI workflow or a file in this repository demonstrates. Limits and
things that have not been exercised are listed at the end.

## Contents

- [Run it](#run-it) · [Configuration](#configuration) · [Who can do what](#who-can-do-what)
- [Rules the system enforces](#rules-the-system-enforces) · [Matching](#matching) · [Security](#security)
- [Quality gates](#quality-gates) · [Docker](#docker) · [Design decisions](#design-decisions)
- [Project layout](#project-layout) · [Changes since the first release](#changes-since-the-first-release) · [Known limits](#known-limits)

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Adiie0001/TeamLocum.git
cd TeamLocum
dotnet run --project TeamLocum.Web
```

`dotnet run` uses the `Development` environment (see `Properties/launchSettings.json`): it needs no secrets, creates
`teamlocum.db`, applies the migrations and inserts demo data: three hospitals, five doctors, five shifts (one finished,
one filled, three open, one of them overnight), applications and the 2026–27 England and Wales bank holidays.

| URL | What |
|---|---|
| http://localhost:5115 | The application |
| http://localhost:5115/health/live | Liveness: the process is up (no database access) |
| http://localhost:5115/health/ready | Readiness: the database is reachable (503 otherwise) |

Demo accounts, **Development only** (the application refuses to start with demo data or a published demo password in
any other environment):

| Role | Email | Password |
|---|---|---|
| Administrator | `admin@example.com` | `Admin@Demo12345` |
| Hospital | `city.hospital@example.com` | `Hospital@Demo12345` |
| Doctor | `dr.sharma@example.com` | `Doctor@Demo12345` |

All demo addresses use the reserved `example.com` domain and the demo doctors' GMC numbers are deliberately invalid
placeholders. Hospitals and doctors can also register themselves; their accounts then wait for an administrator.

## Configuration

Settings come from `appsettings*.json`, environment variables (`Section__Key`) and user-secrets, in the usual
ASP.NET Core order. The application validates them at startup and refuses to run on a bad combination, saying why.

| Setting | Default | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Data Source=teamlocum.db` | SQLite |
| `Database__ApplyMigrationsOnStartup` | `false` (`true` in Development and in the Docker image) | Otherwise run `dotnet ef database update`; the app refuses to start with pending migrations |
| `Seed__DemoData` | `false` | Allowed only in Development |
| `Seed__AdminEmail`, `Seed__AdminPassword` | none | Creates the first administrator once, if none exists. Both or neither; the password must pass the policy and may not be a demo password |
| `Clinic__TimeZone` | `Europe/London` | IANA zone in which shift times are entered and "now" is judged |
| `RateLimiting__Enabled`, `__SignInPerMinute`, `__GlobalPerMinute` | on, 10, 300 | Per client address, in memory. Sign-in and registration posts have their own allowance; probes and static files are not counted |
| `Security__HttpsRedirection` | `false` | Turn on when Kestrel itself terminates TLS; usually a proxy does |
| `DataProtection__KeysPath` | none | Folder for the keys behind cookies and anti-forgery tokens. Set it on a persistent volume or every restart signs everyone out |
| `Matching__MaxResults` | 10 | Suggestions shown per shift |
| `Matching__Gemini__Enabled`, `__ApiKey`, `__Model` | off | See [Matching](#matching) |

Behind a reverse proxy you control, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so rate limiting sees the real
client address and cookies are marked `Secure` (the Docker image does this). Never expose such an instance directly:
the setting trusts whatever `X-Forwarded-*` headers arrive, so a direct client could pretend to be someone else. The app formats dates and money for `en-GB` and needs
ICU and time-zone data on the machine, which a normal Linux, Windows or macOS .NET install has; do not run it with
invariant globalization (a test guards the published settings).

## Who can do what

| | Hospital | Doctor | Administrator |
|---|---|---|---|
| Register themselves | yes, then waits for approval | yes, then waits for approval | no, never through the form |
| Post and edit shifts | when approved | no | for any approved hospital |
| See shifts | its own | open ones, ones it applied for, its own | all |
| Apply / withdraw | no | when approved | no |
| Assign, release, cancel, complete | its own shifts | no | all |
| Browse doctors | approved doctors, with GMC number | no | all, with email and status |
| Approve, reject, suspend, reinstate | no | no | hospitals and doctors |
| Bank holidays | read | read | add and remove |
| Unlock a locked account | no | no | yes |

Doctors never see each other's names: they see "You" on their own shifts and a neutral label otherwise. Anything that
belongs to someone else answers 404, not "forbidden", so identifiers cannot be probed.

## Rules the system enforces

- **No overlapping shifts for a doctor.** A doctor cannot hold two overlapping filled or completed shifts. The service
  checks first so it can explain the clash (at apply and assign time, and when editing a filled shift's times). Two
  SQLite triggers (`booking_overlap`) are the backstop, and a test sends two hospitals racing to assign one doctor to
  overlapping shifts: exactly one wins. Back-to-back shifts are allowed.
- **Shift lifecycle.** Open → Filled → Completed, or Cancelled from Open or Filled. Illegal moves throw a domain
  exception and change nothing; the same applies to hospital and doctor approval states. Cancelling or filling
  declines the other pending applications; releasing a doctor declines theirs.
- **Time is clinic wall-clock time.** Shifts have a start and an end (so an overnight shift is ordinary), at most 24
  hours long. "Has it started?" and "has it finished?" are judged in the clinic's zone, not the server's.
  Bank holidays are flagged on the shift and never block anything.
- **Concurrent edits.** Every shift carries a concurrency stamp; a stale form is refused rather than overwriting.
- **Database constraints.** `CHECK` constraints reject empty windows, non-positive rates, filled shifts without a doctor
  and open shifts with one; GMC numbers, application pairs and bank-holiday dates are unique; deleting a hospital or a
  doctor who has shifts is blocked. Money is stored as whole pence and timestamps as Unix milliseconds.
- **Approval gates.** A hospital that is not approved cannot post, edit or assign; a doctor that is not approved cannot
  apply or be assigned. Approval is re-read from the database on every request, so a suspension takes effect at once.

## Matching

Each open shift shows "Suggested doctors": approved doctors who are free at those times, ranked, with the reason.

| `source` shown on the page | Meaning |
|---|---|
| rules | The deterministic scorer answered. AI is not configured; nothing left the server |
| gemini | The model ranked anonymised profiles and the answer passed validation |
| rules-fallback | AI is configured but failed (timeout, HTTP error, open circuit breaker, unreadable or invalid answer), so the rules answered |

**Rules** (`RuleBasedShiftMatcher`): five factors add up to 100: specialty 40 (synonyms such as GP, A&E, Paeds are
understood; partial credit for related words), location 25 (a shared meaningful place name; words such as "hospital"
and "general" are ignored), rate 15 (half credit within ten per cent of the doctor's minimum), experience 10 (a point
a year) and the doctor's own interest 10. Ties break by interest, then name, then id, so the same inputs always give
the same ranking. Every factor is listed under "Score breakdown".

**Gemini** (optional, off by default): enable with `Matching__Gemini__Enabled=true` and `Matching__Gemini__ApiKey`.
Candidates are sent as opaque references (`c1`, `c2` …) with specialties, base location, minimum rate, experience and
whether they applied. Names, email addresses, GMC numbers, internal ids, the hospital and the shift notes never leave
the server; a test inspects the outgoing request for exactly that. The key travels in the `x-goog-api-key` header,
never in the URL. The answer is untrusted: references must be ones that were sent, scores are clamped to 0–100 and
reasons are reduced to short plain text (and HTML-encoded when shown). Calls go through a resilience pipeline
(timeout, one retry, circuit breaker) and answers are cached for five minutes per question.

## Security

- Every state-changing request is a POST that needs an anti-forgery token (a global filter, so a new action cannot
  forget it); links never change data.
- Sessions are cookies (`HttpOnly`, `SameSite=Lax`, 8 hours sliding, `Secure` on HTTPS); the anti-forgery and flash
  cookies are `HttpOnly` too. Five wrong passwords lock an account for 15 minutes and an administrator can unlock it.
  Passwords need 10+ characters with upper case, lower case, a digit and four different characters. Sign-in failures
  say the same thing for unknown accounts and wrong passwords.
- Roles are enforced twice: by attributes on the controllers and, more importantly, inside the services, which take
  an `Actor` and decide what that actor may see or do, so the rules hold for any future caller.
- Content-Security-Policy allows only this site's own scripts and styles (no inline script, style or event
  handlers, and a test loads the main pages of every role to prove it), plus `X-Frame-Options: DENY`, `nosniff`, a strict referrer
  policy and `Cache-Control: no-store` on signed-in pages.
- Rate limits per client address; request bodies are limited to 2 MB.
- Errors show a generic page with a reference (the `X-Correlation-ID`, which is also in every log line); stack
  traces and messages never reach the browser outside Development. Callers may send their own id (letters, digits,
  `-_.`, up to 64 characters).
- Identity pages that depend on features this app does not have (email confirmation, password reset by email,
  external logins, two-factor set-up, self-service account deletion) answer 404 instead of promising something.
- Secrets (the bootstrap administrator's password, the Gemini key) come from the environment and are never in the
  repository. See [SECURITY.md](SECURITY.md).

## Quality gates

```bash
dotnet build -warnaserror      # zero warnings
dotnet test                    # integration + unit tests
```

The integration tests start the real application in-process against a temporary SQLite file with the real migrations
applied, and swap only the clock and the Gemini HTTP endpoint. They cover, among other things: the main pages against
every role, other people's data (IDOR) for shifts and doctors, anti-forgery rejection, registration and lockout,
every transition of the shift lifecycle, the concurrent-assignment race, the database triggers and constraints written
to directly, listing filters and paging, the matching rules and every Gemini failure path, what is sent to Gemini,
rate limits, security headers, error pages, health probes and refusal to start on unsafe configuration. A test fails if
the EF model changes without a migration.

CI (`.github/workflows/dotnet.yml`) runs the build with warnings as errors, the tests with a coverage gate (**line
≥ 96 %, branch ≥ 88 %, method ≥ 96 %**; measured 99.6 / 92.1 / 99.4 when the gate was set, with generated migrations
excluded), and a Docker job that builds the image, starts it and probes `/health/ready`, the sign-in page and a
stylesheet. CodeQL and Dependabot are configured.

## Docker

```bash
cp .env.example .env     # set ADMIN_EMAIL and ADMIN_PASSWORD (and optionally the Gemini settings)
docker compose up --build
# http://localhost:8080/health/ready
```

The image is a multi-stage build that runs as a non-root user and keeps the SQLite file and the cookie keys on one
volume; migrations run at start. TLS must end at a reverse proxy in front of it. Status: CI builds the image and
probes the running container on every push; `docker compose up` itself has not been exercised end to end locally.

## Design decisions

| Decision | Why |
|---|---|
| Authorisation inside the services, with an `Actor`, as well as on controllers | Rules do not depend on which controller, test or future API calls the service |
| Overlap rule enforced three ways (service check, database trigger, race test) | The friendly message and the guarantee are different jobs; the trigger holds when two requests arrive together |
| 404 for other people's records | A hospital cannot learn that someone else's shift or a doctor exists |
| Shifts as `StartsAt`/`EndsAt` in clinic wall-clock time | Overnight shifts are ordinary, and "started" means started where the clinic is |
| Rule scorer as the default, AI as an optional layer with an honest label | The feature works offline and in tests; AI failure degrades visibly instead of silently |
| Anonymised AI input, untrusted AI output | A language model sees what it needs to rank and nothing that identifies a person; it cannot name a doctor that was not sent |
| Money as integer pence | SQLite stores decimals as text, which cannot be ordered or summed correctly |
| Identity pages switched off rather than half-working | No mail provider is configured, so those pages would only pretend |
| `TimeProvider` everywhere | Lockout, cookie expiry, "has it started" and caching are tested by moving the clock, not by sleeping |
| Migrations applied by opt-in config, never implicitly in production | The operator decides when a schema changes |

## Project layout

```
TeamLocum.Web/
  Areas/Identity/Pages/   Sign-in and registration, account settings (framework pages restyled into the app layout)
  Controllers/            Thin MVC controllers
  Domain/                 Entities with their state machines, enums, domain exceptions
  Data/                   DbContext, migrations (with the overlap triggers), startup initializer, demo data
  Services/               BookingService, DirectoryService, Actor, ClinicClock
  Services/Matching/      Rule scorer, specialty normaliser, Gemini client and matcher, fallback selector
  Infrastructure/         Options + validators, middleware, error filter, startup wiring
  ViewModels/  Views/  wwwroot/
TeamLocum.Tests/          xUnit tests (integration + unit) and the test host support code
```

## Changes since the first release

The first release was a demo with a working happy path; these changes are deliberate and some are breaking:

- Targets .NET 10 (LTS).
- **Authorisation.** The first release required sign-in only: any signed-in user could approve hospitals and doctors,
  create, edit or delete shifts, hospitals and doctors, several of those actions were plain links (so a page could
  trigger them), and a "masquerade" stub existed. Those routes are gone. Now every action checks the actor's role
  and ownership in the service layer, every POST needs an anti-forgery token, and approvals gate what an account can
  do.
- **Overlap rule.** The first release checked overlaps only when suggesting doctors; creating or editing a booking did
  not enforce them, and a date plus two times of day could not express an overnight shift. Shifts are now
  `StartsAt`/`EndsAt`, and the rule is enforced in the service and in the database.
- **Matching.** The first release's "AI" matching was a simulation: scores came from a hash of the doctor, the notes and
  the location, and the explanations were canned text. It is replaced by the transparent rule scorer above plus an
  optional Gemini integration with anonymised input, validated output and an honest `source`.
- Hospitals and doctors register themselves and wait for approval (was seeded accounts only); accounts lock after
  repeated failures; the demo credentials changed and exist only in Development.
- The SQL Server provider, the `GetAvailableLocums` endpoint and the CRUD pages for doctors and hospitals were removed.
- Statuses are enums stored as text, money is stored as pence, and the schema has constraints and a migration.
- The first release's 6 unit tests (five small tests and one empty template test) were replaced by the suite described above.

## Known limits

- SQLite is a single-node database; the in-memory rate limiter and the Gemini cache are per instance.
- There is no email: no address verification, no password reset by email, no two-factor sign-in. Locked accounts are
  unlocked by an administrator.
- There is no self-service export or deletion of personal data. An account with shift history cannot be deleted without
  an administrator and a decision about retention; a real service would need both.
- GMC numbers are checked for format (seven digits) and uniqueness, not against the GMC register; the shift page tells
  hospitals to check the public register themselves. The matcher knows nothing about indemnity, DBS checks or
  competencies. It is decision support, not a decision.
- Pay shown is the hourly rate times the scheduled hours; it is not a payroll calculation. Durations are wall-clock
  differences, so a shift that spans a clock change is an hour shorter or longer than the time actually worked.
- Bank holidays are maintained by an administrator; the demo data covers England and Wales from December 2026 to
  December 2027 only.
- The Gemini request and response handling is tested against a fake HTTP endpoint only; it has not been run against the
  live service in CI.
- The cookie keys on the Docker volume are not encrypted at rest, and TLS termination, backups and monitoring are left
  to the deployment platform.
- Observability is logs (JSON outside Development, with the correlation id) and health probes; there is no metrics or
  tracing export. Accessibility has not been audited beyond using labelled form controls and Bootstrap's defaults.
- The Docker files are built and probed in CI but `docker compose up` has not been run end to end locally.

## Author

Aditya Maisuriya · [Portfolio](https://adityamaisuriya.pages.dev) · [GitHub](https://github.com/Adiie0001) ·
[LinkedIn](https://linkedin.com/in/aditya-maisuriya-39a540202)
