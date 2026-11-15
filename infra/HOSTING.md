# Hosting outside Azure App Service

The app was built for Azure App Service ([DEPLOY.md](DEPLOY.md)), and that deployment keeps
working unchanged. It can also run anywhere that runs a container: Fly.io, Render, Railway,
DigitalOcean App Platform, Azure Container Apps, Google Cloud Run, Cloudflare Containers, or your
own server with Docker. This page covers what it needs there and how to set it up.

**Setting up a new troop?** [SETUP.md](SETUP.md) is the step-by-step guide for the one-click
Render setup (`render.yaml`); this page is the full reference behind it.

## What the app needs

| Need | Options |
|---|---|
| **Somewhere to run the container** | Any container host. 1 GB of memory is comfortable; 512 MB is tight once PDFs are drawn. Run **one copy** (see [One copy at a time](#one-copy-at-a-time)). |
| **A database** | PostgreSQL 14+ (Neon, Supabase, Railway, Render, Fly, or a container) or SQL Server (Azure SQL, SQL Server 2019+ in a container, which needs 2 GB of memory of its own). See [The database](#the-database). |
| **File storage** for receipts | An S3-compatible bucket (Cloudflare R2, AWS S3, Backblaze B2, ...), Azure Blob Storage, or a folder on a persistent disk. |
| **Backup storage** | A *second*, separate bucket or storage account. Optional, but don't skip it. |
| **Email** | Any SMTP service (Resend, SendGrid, Brevo, Mailgun, Amazon SES, ...) or Azure Communication Services. |
| **Admin sign-in** | A Microsoft and/or Google sign-in app registration, with this host's address added (see [After the first deploy](#after-the-first-deploy)). |

## Try it on your own machine first

`docker compose up --build` from the repository root starts the app with SQL Server and Mailpit
(an email catcher). With PostgreSQL instead (lighter, and runs natively on Apple-silicon Macs):
`docker compose -f docker-compose.yml -f docker-compose.postgres.yml up --build`. Open http://localhost:8080. Admin sign-in is the fake local one, guardian
sign-in codes appear at http://localhost:8025, and receipts and backups go to Docker volumes. It
runs in the Development environment with placeholder passwords, so it's for trying things out
only. Never put it on the internet as it is.

## Settings

Every setting is an environment variable on the host. A `:` in a setting's name becomes `__`
(two underscores), e.g. `Storage:S3:Bucket` is `Storage__S3__Bucket`. Keep secrets in the host's
secret store (Fly secrets, Render environment groups, and so on), never in the repository.

### Always

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Provider` | `Postgres` for PostgreSQL. Leave unset for SQL Server. |
| `ConnectionStrings__FinanceDb` | The database's connection string. PostgreSQL: `Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require`, or the `postgresql://user:password@host/db?sslmode=require` URL most hosts give you. SQL Server: `Server=...;Database=...;User Id=...;Password=...` |
| `Auth__LoginCodePepper` | A long random string (`openssl rand -base64 48`). **Keep it forever**: changing it unlinks every guardian's authenticator app. Moving from Azure? Copy the existing value. |
| `Auth__BootstrapEditors__0` | Your email address, so you can sign in as the first Editor |
| `Auth__GlobalAdmins__0` | The system admin's email address (see [DEPLOY.md](DEPLOY.md#who-gets-access)). Required. |
| The `Troop__*` settings | See [Your troop's settings](#your-troops-settings). |

The Dockerfile already sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, which makes the app trust
the host's HTTPS proxy. Don't unset it, or sign-in will break.

### Your troop's settings

Nothing about a particular troop is written into the app: its name, address and contact all
come from these. A Production site won't start until the required ones (and
`Email__SenderAddress` and `Auth__GlobalAdmins__0`) are set, and says which are missing.

| Variable | Required | Example | Used for |
|---|---|---|---|
| `Troop__Name` | yes | `Troop 52` | Page headings, email subjects ("Troop 52 Finance: ..."), PDFs, letters |
| `Troop__SiteUrl` | yes (except on Render, which supplies its own address until you set this) | `https://finance.troop52.org/` | Links in emails, the letterhead |
| `Troop__ContactEmail` | yes | `treasurer@troop52.org` | "Questions?" on bills, letters, the Terms and Privacy pages |
| `Troop__TimeZone` | yes | `America/Chicago` | Every date shown, month boundaries in reports, the nightly backup's 2 AM. An [IANA name](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones). |
| `Troop__ShortName` | no | `52 Finance` | The name under the icon when installed on a phone. Defaults to the number + "Finance". |
| `Troop__IconText` | no | `52` | The few characters drawn under the mark on the home-screen icon. Defaults to the number from `Troop__Name`; none if there isn't one. |
| `Troop__AuthenticatorName` | no | `T52 Finance Guardian` | How the site is listed in guardians' authenticator apps. Defaults to the short name + "Guardian". Changing it later only renames new entries; existing ones keep working. |
| `Troop__CharteredOrganization`, `Troop__Ein` | no | | Donation thank-you letters (shown as placeholders to fill in until set) |

Before going live, read the Terms (`/terms`) and Privacy (`/privacy`) pages on your site. They fill
in your troop's name and contact, but the rest is one troop's wording: in particular the
Privacy page names the services the data goes to (Azure and Azure email), which differ if you
host elsewhere. Change `Components/Pages/Terms.razor` and `Privacy.razor` to match.

### Admin sign-in (at least one)

| Variable | Value |
|---|---|
| `AzureAd__ClientId` | Microsoft sign-in app's client id ([DEPLOY.md step 2](DEPLOY.md#2-register-the-sign-in-apps-admintreasurer-login)) |
| `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | Google sign-in |

### File storage: `Storage__Provider`

**`S3`** (Cloudflare R2, AWS S3, Backblaze B2, ...). The bucket must already exist and be private.

| Variable | Value |
|---|---|
| `Storage__Provider` | `S3` |
| `Storage__S3__Bucket` | e.g. `troop-finances` |
| `Storage__S3__ServiceUrl` | R2: `https://<account-id>.r2.cloudflarestorage.com`; B2: `https://s3.<region>.backblazeb2.com`; leave empty for AWS |
| `Storage__S3__Region` | R2: `auto`; AWS/B2: the bucket's region, e.g. `us-west-2` |
| `Storage__S3__AccessKey`, `Storage__S3__SecretKey` | An access key limited to this bucket |
| `Storage__S3__Prefix` | Optional folder inside the bucket |

**`FileSystem`**: a folder on a disk that survives restarts and redeploys (a Fly volume, a VPS
disk). `Storage__FileSystem__Root` is the folder, e.g. `/data/files`. The container runs as a
non-root user (uid 1654), which must be able to write there.

**`Azure`** (the default): `ConnectionStrings__AzureStorage` set to the storage account's
connection string.

### Backups: `BackupStorage__Provider`

The same choices and settings under `BackupStorage__`, e.g. `BackupStorage__Provider=S3`,
`BackupStorage__S3__Bucket=troop-finances-backups`, and so on. Or an Azure storage account
through `ConnectionStrings__BackupStorage`, as on App Service. Use a **different** bucket from the
receipts, ideally with a different provider or account, so one mistake or outage can't take
both. With neither setting, backups are off and Settings → Backups says so.

Backups run inside the app every night after 2:00 AM Pacific. That needs the app to be running
at 2 AM. If your host stops it when idle, see [Letting the app sleep](#letting-the-app-sleep).

### Email: `Email__Provider`

| Variable | Value |
|---|---|
| `Email__Provider` | `Smtp` |
| `Email__SenderAddress` | e.g. `noreply@troop52.org`. The domain must be verified with the email service (SPF/DKIM records) or mail goes to spam. |
| `Email__Smtp__Host` | e.g. `smtp.resend.com`, `smtp.sendgrid.net`, `smtp-relay.brevo.com` |
| `Email__Smtp__Port` | `587` (default) or `465` |
| `Email__Smtp__Username`, `Email__Smtp__Password` | From the email service (Resend: `resend` and an API key) |
| `Email__Smtp__Security` | `Auto` (default), `StartTls`, `SslOnConnect` |

Or `AzureCommunicationServices` with `ConnectionStrings__AzureCommunicationServices`, as on App
Service.

## The database

The app runs on either engine; `Database__Provider` says which. It behaves the same on both.

**PostgreSQL** is the cheaper choice off Azure: most hosts include it, often free at this size.

- **Hosted**: Neon (free tier, and it sleeps when idle like the app can), Supabase, or the
  host's own (Railway, Render, Fly). Use the *direct* connection string, not a "pooler" or
  "transaction mode" one: the app keeps its own small pool of connections, and runs some work
  in transactions that a transaction-mode pooler can break.
- **In a container beside the app**: the `postgres:17` image needs little memory (about 100 MB
  at this size). As with any self-run database, you look after updates and backups beyond the
  app's own nightly export.

**SQL Server**:

- **SQL Server in a container beside the app** (a VPS with Docker Compose): cheapest, but you
  look after it: updates, and backups beyond the app's own nightly export. The free Express
  edition (`MSSQL_PID=Express`) is plenty. It needs 2 GB of memory on top of the app's.
- **The existing Azure SQL database from another host**: today it only accepts the App Service's
  managed identity, from its own subnet. To reach it from elsewhere, (1) create an app
  registration with a client secret, add it to the database as a user, and connect with
  `Authentication=Active Directory Service Principal;User Id=<app id>;Password=<secret>`, and
  (2) add a firewall rule for the host's outgoing IP address. Most container hosts don't have a
  fixed outgoing IP address, which makes (2) hard; a static egress IP add-on, or Cloudflare
  Tunnel, solves it.
- **Managed SQL Server elsewhere** (AWS RDS for SQL Server Express, etc.): works, though hosted
  SQL Server usually costs more than hosted PostgreSQL.

On first start the app creates or updates its own tables. Moving data from the Azure database
(to either engine): take a backup there (Settings → Backups → Run backup now), then restore it
into the new database with `restore-backup` ([DEPLOY.md step 7](DEPLOY.md#restoring)), using
`--target "<new connection string>"`, `Database__Provider=Postgres` when the new one is
PostgreSQL, and the new host's storage settings. Backups are the same on both engines, so this
works in either direction. Start the app against the new database once first, so its tables
exist.

## Sign-in keys

The keys that sign admin and guardian sign-in cookies are kept in the database
(`DataProtectionKeys` table), so restarts and redeploys don't sign anyone out, and moving the
database to a new host keeps everyone signed in too. The nightly backup leaves the keys out on
purpose (a backup file shouldn't carry them). After a restore into a new database, everyone just
signs in again.

## Letting the app sleep

Hosts that stop the container when nobody is using it (Fly.io auto-stop, Azure Container Apps or
Cloud Run with zero minimum instances, Cloudflare Containers) cost far less than keeping it
running. An open page counts as use, so the app isn't stopped under anyone. The first visitor
after a quiet spell waits a few seconds while it starts.

The nightly backup can't run on a timer inside a stopped app, so start it from outside:

1. Set `Backup__Schedule=External`, and `Backup__TriggerKey` to a long random string
   (`openssl rand -hex 32`; at least 32 characters).
2. Have something call it once a night after 2:00 AM Pacific:
   ```bash
   curl --fail -X POST -H "Authorization: Bearer $BACKUP_TRIGGER_KEY" https://<your-site>/internal/backup
   ```
   The repository includes a GitHub Actions workflow that does this
   ([.github/workflows/nightly-backup.yml](../.github/workflows/nightly-backup.yml)). It runs once
   you add the repository variable `BACKUP_TRIGGER_URL` (`https://<your-site>/internal/backup`)
   and the secret `BACKUP_TRIGGER_KEY` under GitHub → Settings → Secrets and variables → Actions.
   A host's own cron feature or a Cloudflare Cron Trigger works just as well.

The call wakes the app and stays open until the backup finishes, so the host keeps the app
running for that long. Calling it again the same day does nothing. Settings → Backups shows a
warning if a night is missed.

Use a database that can also sleep (Neon, or Azure SQL serverless), or most of the saving is
lost.

## One copy at a time

Each open page keeps a live connection to the copy of the app that drew it. Run a single
instance (no autoscaling beyond one). Two or more need sticky sessions from the host, and
nothing here needs more than one.

## After the first deploy

1. **Sign-in redirect addresses.** Add `https://<your-site>/signin-oidc` to the Microsoft app
   registration and `https://<your-site>/signin-google` to the Google client.
2. **Email domain.** Verify the sender's domain with the email service.
3. **Health check.** Point the host's health check at `GET /healthz`.
4. **Check it.** Sign in at `/admin`, send yourself a guardian sign-in code at `/portal/login`,
   then Settings → Backups → Run backup now and confirm it completes.
