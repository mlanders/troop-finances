# Troop Finances

A Blazor Server app tracking troop money: reimbursement receipts adults submit, invoices scouts
owe (trips/camp/meals), and fundraiser proceeds credited into individual scouts' accounts.

## Set it up for your troop

Any Scouts BSA troop can run its own copy, with its own database: nothing about a particular
troop is written into the app. The quickest way is Render (about $13 a month):

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/sacparker09/troop-finances)

Before clicking, read [infra/SETUP.md](infra/SETUP.md): it takes about 30 minutes, mostly
setting up Google sign-in and an email service, which no host can do for you. Other hosts and
Azure are covered in [infra/HOSTING.md](infra/HOSTING.md) and [infra/DEPLOY.md](infra/DEPLOY.md).
Free to use under the [MIT License](LICENSE).

Sibling project to `bsa-meeting-signin`, reusing many of its patterns (Blazor Server, repository
pattern, Bicep/`deploy.ps1` deployment flow) but deployed and maintained
independently, since this app handles financial/PII data with different access needs. Unlike the
sign-in app, its records live in **Azure SQL** (through EF Core) rather than Table Storage, so
lists, totals and reports are database queries that stay fast as years of history build up, and
every money operation is one all-or-nothing transaction. Receipt files are in Blob Storage.

## Who logs in, and how

- **Treasurer/admin** (`/admin`) -- signs in at `/admin/signin` with a Microsoft account (work,
  school or personal) or a Google account, the same sign-in page as the meal-planner app.
  Manages everything: reviews/approves reimbursements, issues invoices, records fundraiser
  allocations, manages the scout/guardian roster. Can also submit a receipt on a guardian's
  behalf (`/admin/reimbursements/new`); it shows up in that guardian's portal.

  The **Invoices** and **Reimbursements** lists filter by status (always shown) and, behind the
  **Filters** button, by date range, scout or guardian, and description. Any admin can save the
  current filters as a named **view** (e.g. "Summer camp 2026": description "summer camp", issued
  Jun 1 - Aug 31) that every admin can then pick from the View list; `?view=<id>` links straight
  to one. Whoever saved a view, or any Editor, can delete it. Views live in the `ListViews` table
  (backed up nightly), and saving or deleting one is recorded in Activity History.

  Signing in alone grants nothing. Access is stored in the app's `AdminUsers` table and managed
  by Editors at **Settings > People & access** (`/admin/settings/access`):
  - **Read only** can view everything; **Edit** can also make changes, including who has access.
  - Someone signing in for the first time with no role sees a "contact an administrator" page
    and appears on that settings page as an **access request**, where an Editor gives them a
    role or dismisses the request.
  - One person can be marked **committee chair**, who gets a copy of the "Edit access granted"
    email (see below).
  - Editors can't change their own access, and the last Editor can't be demoted or removed.
  - **Global admin:** addresses in `Auth:GlobalAdmins` (provider-verified, like the bootstrap
    list) get Read only and can always turn on temporary Edit. They can also allow others to, even
    while Read only. It's set only from configuration; People & Access shows a "Global admin" badge.
  - **Temporary edit:** a Global admin, or a Read-only person an Editor or Global admin has
    allowed, can turn on Edit for themselves for 1, 2, 4 or 8 hours (`/admin/settings/edit-access`),
    giving a reason. Each time emails the standing Editors, the committee chair and
    `Auth:EditorGrantNotify`, always (there's no switch to turn this alert off). It's recorded in
    Activity History, ends on its own, and can't
    change who has access. Their stored role stays Read only, so it never counts as an Editor for
    the last-Editor or bootstrap checks.
    Allowing someone takes a **last day** (today up to a year out, Pacific). After that day they
    can't turn it on, and any Edit they have on ends at midnight. People & Access shows the date
    and lets an Editor change it or stop it early. A Global admin's own permission has no end date.
  - The very first Editor comes from `Auth:BootstrapEditors` (appsettings), and only while no
    Editor exists yet: a listed address signing in becomes an Editor. The address must be one
    the provider vouches for: Google-verified, or a Microsoft account's sign-in name.
  - The role is looked up on every request, not stored in the sign-in cookie, so a change applies
    within a minute or two, open pages included (`AdminRoleClaimsTransformation`,
    `AdminRevalidatingAuthStateProvider`).
- **Parents/guardians** (`/portal`) -- a passwordless **emailed one-time code**, no Microsoft
  account required. Once linked to a scout (by the treasurer, under `/admin/guardians`), a parent
  can submit their own reimbursement receipts and see that scout's balance/invoice history.
  The sign-in lasts 30 days and renews as they use it, so the home-screen app (below) doesn't
  ask for a new code every time.
  As an alternative, a parent can link an **authenticator app** (Google/Microsoft Authenticator,
  1Password, Apple Passwords...) under **Sign-in options** (`/portal/sign-in-options`, reached by
  tapping their name in the header), then sign in with their email plus the app's current code
  via "Use my authenticator app" on the sign-in page. Emailed codes always keep working, so a lost
  phone never locks anyone out. From a guardian's row menu on the Guardians list, an Editor can
  also **set up the app in person** (show the QR code for the parent to scan with their phone,
  then type in the code it shows) or remove a parent's app; both are recorded in Activity History.
  The shared secret is stored AES-GCM-encrypted with a key derived from `Auth:LoginCodePepper`;
  each code works once, and five wrong codes lock app sign-in for 15 minutes.

## On a phone: the home-screen app

The site can be installed as an app (a PWA) named for the troop (`Troop:ShortName`, e.g. **52
Finance**). Its mark is the fleur-de-lis with a gold dollar coin (`wwwroot/favicon.svg`, also the
browser-tab icon and sign-in logo); the home-screen icons (`/icons/*.png`, drawn by
`Services/TroopIcons`) add the troop's number beneath it. Once signed in, parents and leaders
see an "Add 52 Finance to your home screen" banner. On Android, Chrome and Edge it has an
**Install** button. On iPhone and iPad it explains Safari's **Share > Add to Home Screen**, or
asks people to open the page in Safari if they're in another browser. **Not now** hides it for 30
days on that device. The banner never shows inside the installed app.

- `wwwroot/manifest.webmanifest` and `wwwroot/icons/` hold the name, colors and icons.
  `App.razor` adds the apple-* tags iOS needs.
- `wwwroot/sw.js` is a deliberately small service worker. Blazor Server needs a live
  connection, so it caches nothing but `offline.html`, which it shows when a page can't load.
  Bump `CACHE_VERSION` when that page changes.
- `wwwroot/pwa-install.js` registers the service worker and drives the banner
  (`Components/InstallPrompt.razor`).
- The app opens at `/`, which goes straight to `/portal` or `/admin` when the person is signed
  in to just one of them.
- On phones the portal's links become a tab bar along the bottom. The admin area gets one too,
  as in the troop's Hub app: Home, Reimburse, Invoices and Families, plus **More**, which opens a
  panel from the bottom with Fundraising, Bank, Reports, Settings and Sign out
  (`AdminLayout.razor`; `mobile-nav.js` closes it after a tap). The layouts pad for notches
  and the home indicator (`env(safe-area-inset-*)`).
- On iOS the installed app keeps its own cookies apart from Safari, so a parent signs in once
  more inside the app.

## Data model, in short

- Each **scout** has a running account balance, backed by an append-only **ledger**
  (`ScoutLedger` table) -- fundraiser allocations credit it, invoices can debit against it. The
  ledger is the source of truth; the scout's `CachedBalance` field is a denormalized read
  optimization kept in sync automatically (see `Data/Repositories/ScoutLedgerRepository.cs` and
  `Services/ScoutAccountService.cs`).
- Normal flows (issuing an invoice, allocating a fundraiser credit) can never push a balance
  negative -- they clamp to what's actually available. A treasurer-only **manual adjustment**
  (with a required reason) is the one path that can, for corrections.
- Payments are **record-keeping only** -- no payment processor integration. Reimbursements/
  invoices get marked paid manually after the real payment happens outside the app.
- Every change that moves money (issuing or voiding an invoice, recording a payment or family
  payment, allocating fundraiser proceeds, a manual adjustment) is **one database transaction**:
  the ledger entry, cached balance, invoice, payment rows and audit entry are saved together or
  not at all (see `Data/FinanceDb.cs`). Emails go out only after it commits.
- Receipts are uploaded to a **private** Blob Storage container; nothing is publicly accessible or
  served via SAS token -- every download is mediated by an authenticated, ownership-checked
  endpoint (see the `/admin/reimbursements/{id}/receipt` and `/portal/reimbursements/{id}/receipt`
  routes in `Program.cs`).

## Email

Every email goes through `Services/EmailNotificationService.cs`, which checks the per-type on/off
setting, sends, and records a summary row (no message body) in the `EmailLog` table:

- **Login code** -- to the guardian signing in. Always on.
- **Authenticator app added or removed** -- to the guardian, whenever an authenticator app is
  linked to or removed from their sign-in. Always on.
- **Receipt submitted** -- to the admin notification addresses and the submitting guardian.
- **Receipt status update** (approved, rejected, paid) -- to the submitting guardian.
- **Invoice** -- to the scout's active linked guardians when an invoice is issued.
- **Edit access granted** -- to every Editor, the committee chair and the `Auth:EditorGrantNotify`
  addresses (appsettings) when someone is given Edit access. **Off** until turned on.

`/admin/settings` (the Settings link next to Sign out) is where all but the login code can be turned on or off, the admin notification addresses are
maintained, and the last 3 months of sent email can be reviewed and filtered by "to" address. A failed
notification email is logged as Failed but never blocks the action that triggered it.

See the plan this was built from (or the doc comments throughout `Data/Entities/` and
`Services/`) for the full schema and design rationale.

## Local development

**Prerequisites:** .NET 10 SDK, SQL Server LocalDB (installed with Visual Studio; check with
`sqllocaldb info`), and Node.js for the Azurite storage emulator, which stores receipt files.

1. Run Azurite:
   ```bash
   npm install -g azurite
   azurite --silent --location ./azurite-data --debug ./azurite-data/debug.log
   ```
2. `appsettings.Development.json` already points the database at LocalDB
   (`ConnectionStrings:FinanceDb`, database `BsaTroopFinances`) and receipts at Azurite
   (`UseDevelopmentStorage=true`). On startup the app creates or updates the database from the EF
   Core migrations in `Data/Migrations` and creates the `receipts` container. After changing an
   entity, add a migration **for each database engine** (the `dotnet-ef` tool is in
   `dotnet-tools.json`; run `dotnet tool restore` once):
   ```bash
   dotnet ef migrations add <Name> --project src/BsaTroopFinances --context FinanceDbContext --output-dir Data/Migrations
   dotnet ef migrations add <Name> --project src/BsaTroopFinances --context PostgresFinanceDbContext --output-dir Data/PostgresMigrations
   ```
   SQL Server's go in `Data/Migrations`, PostgreSQL's in `Data/PostgresMigrations`. A test
   (`Each_engine_has_its_own_migrations_and_both_match_the_model`) fails if either is missing.
3. All emails are **logged to the console, not actually sent**, in Development (see
   `Services/ConsoleEmailSender.cs`) -- no Azure Communication Services resource is needed to
   develop/test the parent portal locally. Watch the console output for `DEV EMAIL (not actually
   sent): to ... -- Your Troop 100 Finance sign-in code -- Your sign-in code is ...` after
   requesting a code.
4. Set the required secrets via user-secrets (never commit these):
   ```bash
   cd src/BsaTroopFinances
   dotnet user-secrets set "Auth:LoginCodePepper" "any-long-random-string-for-local-dev"
   ```
   To test the real admin sign-in locally too, set `AzureAd:ClientId` and `AzureAd:ClientSecret`
   and/or `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` (see
   `infra/DEPLOY.md` step 2). Each provider's button appears only once its client id is set.
5. Run:
   ```bash
   dotnet run --project src/BsaTroopFinances
   ```
6. To actually test the parent portal end-to-end, add a guardian first at `/admin/guardians`
   (requires the sign-in setup from step 4, or the dev sign-in below), link them to a scout, then use that email at
   `/portal/login` -- the code will show up in the console instead of an inbox.

### Admin area without a real sign-in (dev sign-in)

To click through the admin area locally without a Microsoft or Google app registration, use the
`dev-signin` launch profile:

```bash
dotnet run --project src/BsaTroopFinances --launch-profile dev-signin
```

It turns on `DevAuth:Enabled` and `DevAuth:SeedSampleData`:
- `/admin` pages send you to `/dev/signin`, which signs you in as a fake **Editor**. Switch to
  read-only with `/dev/signin?role=Reader`, see the "contact an administrator" page with
  `/dev/signin?role=None`, and sign out at `/dev/signout`. (`/admin/signin` lists all three.)
- On an empty local database it loads a small sample troop (scouts, guardians, a fundraiser,
  invoices and reimbursements in every status) through the app's real services.
- The dev user isn't a real Microsoft or Google sign-in, so it never appears on the
  **People & access** list itself.

This only works in the Development environment: the app refuses to start if `DevAuth:Enabled`
is set anywhere else. Code: `Services/DevAuth.cs`, `Services/DevSampleDataSeeder.cs`.

### A year of test data

To fill the lists (paging, date filters, reports), add a large batch of back-dated activity for
the scouts and guardians already in the local database. Azurite must be running:

```bash
dotnet run --project src/BsaTroopFinances -- seed-test-data [--invoices 250] [--reimbursements 120] [--fundraisers 2] [--seed 441] [--yes]
```

To generate it for the real troop instead of the four sample scouts, first copy just the roster
(scouts, guardians and their links; zero balances, no money records or receipts) from the newest
nightly backup of the live site. It only reads from the backup account, and skips anyone already
in the local database:

```powershell
$env:ConnectionStrings__BackupStorage = "<backup account connection string>"
dotnet run --project src/BsaTroopFinances -- import-roster [<yyyy-MM-dd>] [--yes]
```

That puts real family names and email addresses in your local database (`Services/RosterImportCommand.cs`).

The test data spreads invoices, reimbursements and fundraisers over the last 12 months in every status,
through the app's real services, so balances, ledgers and history stay consistent. Each run adds
more; nothing existing is changed or removed. Everything it creates is recorded by "Test Data",
and no emails are sent. It refuses to run outside Development or against a database or storage
account that isn't on this machine, unless both are named with `--target` and `--receipts-target`
(then the database name has to be typed to confirm).

The same load is available in the site at **Settings > Test data** (Writers only) while the
`TestData:Enabled` setting is on. It is on locally; on a hosted site, add the app setting
`TestData__Enabled=true` while the site holds only test data, and **remove it before going live**:
there is no undo short of restoring a backup. Code: `Services/TestDataCommand.cs`,
`Services/TestDataLoader.cs`, `Services/TestDataGenerator.cs`.

### Tests

```bash
dotnet test
```

`tests/BsaTroopFinances.Tests` has two kinds of tests:
- **Page tests** render admin pages in memory with [bUnit](https://bunit.dev), signed in as a
  fake Editor or Read-only user (`AdminPageTestBase`), with [NSubstitute](https://nsubstitute.github.io)
  fakes for the repositories and services each page uses.
- **Database tests** (`Sql*Tests`) run the real repositories, services, migration and backup
  against a throwaway LocalDB database created per test class (`SqlTestDatabase`): paging and
  totals, stale-update protection, and transactions that must roll back completely when a
  failure is forced partway through. Each skips itself, rather than failing, on a machine
  without LocalDB. To run them against PostgreSQL instead, set `TROOPFIN_TEST_POSTGRES` to a
  server's connection string without a database name, e.g.
  `Host=127.0.0.1;Username=postgres;Password=...` (`docker run -e POSTGRES_PASSWORD=... -p 5432:5432 postgres:17`
  gives you one).
- **Storage tests** (`BlobStoreContractTests`) hold every file store to the same behaviour: a
  temporary folder, an S3 bucket (an in-memory stand-in for the AWS client), and Azure blob
  storage when Azurite is running (skipped otherwise).

Note when writing tests that money inputs fill from the cents like a cash register: typing `50`
means $0.50, so type `50.00`.

## Deploying to Azure

**First-time infrastructure setup**: see [infra/DEPLOY.md](infra/DEPLOY.md) -- Bicep deploy,
Entra app registration, email domain DNS verification, and secret wiring.

**Deploying a code change** after that:
```powershell
.\infra\deploy.ps1
```
from the repo root (PowerShell). See the script's header comment for why it doesn't just use
`Compress-Archive` (a Windows-PowerShell-5.1-specific zip bug that broke deploys on the sibling
app until this was worked out).

**Backups**: nightly to a separate storage account, newest 14 kept; setup and restore steps are
in [infra/DEPLOY.md](infra/DEPLOY.md) step 7.

## Hosting elsewhere

The app also runs as a container (the `Dockerfile` at the root) on hosts other than Azure: Fly.io,
Render, Railway, a VPS, and so on. There it uses SQL Server or PostgreSQL, an S3-compatible
bucket (or a folder) for receipts and backups, and any SMTP service for email, each picked by a
setting (`Database:Provider`, `Storage:Provider`, `BackupStorage:Provider`, `Email:Provider`). It can also be left to sleep
when idle, with the nightly backup started from outside. `docker compose up --build` runs the
whole thing locally. See [infra/HOSTING.md](infra/HOSTING.md).

## Using it for another troop

Nothing about a particular troop is written into the app: the troop's name, web address, contact, time
zone and admins are all settings (`Troop:*`, `Email:SenderAddress`, `Auth:GlobalAdmins`; see
[infra/HOSTING.md "Your troop's settings"](infra/HOSTING.md#your-troops-settings)). Locally it
runs as a sample "Troop 100" (`appsettings.Development.json`); override with user-secrets to see
your own. A test (`No_troop_or_person_is_written_into_the_app`) keeps it that way. Each troop runs
its own copy, with its own database and storage. The code is free to use under the
[MIT License](LICENSE).

## Known limitations / possible next steps

- Automated tests cover the Invoices, Reimbursements and Reports pages, family payments, the
  database layer, the migration, backups and the dev sign-in (see "Tests" above); other admin
  pages are still verified by hand.
- The troop's overall/unallocated fundraiser balance is out of scope by design -- this app only
  tracks money once it's credited to a specific scout's account.
