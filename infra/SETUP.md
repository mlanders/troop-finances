# Set it up for your troop

This puts your own copy of the app online, with its own database. Your troop's records stay in
your copy; nobody else's site can see them. Plan on about 30 minutes, most of it in steps 1 and 2,
which are the same wherever it runs.

**Just looking?** Open the app in [GitHub Codespaces](#try-it-first-github-codespaces) first: it
runs in your browser with sample data, nothing to set up.

## Pick a host

| Host | How | About | Good for |
|---|---|---|---|
| [Render](#render) | Button | $13/month | The simplest: everything in one place, always on. |
| [Railway](#railway) | Button | $5-10/month | Like Render, usually cheaper; billed by use. |
| [DigitalOcean](#digitalocean) | Button | $24/month | Troops already on DigitalOcean. Needs two Spaces buckets made first. |
| [Azure](#azure) | Button, or `azd up` | $17/month | Troops with an Azure subscription (or nonprofit credits). Sleeps when idle; backups kept in a separate account. |
| [Fly.io](#flyio) | A few commands | $2-6/month | The cheapest hosted option, with a free Neon database. Comfortable with a terminal. |
| [Your own server](#your-own-server) | A few commands | $6-12/month | Someone who looks after a Linux server already. |

Each bills whoever creates the account. Prices are for a troop-sized site and change; check the
host's own pricing page. Whichever you pick, the app is the same and you can move later: take a
backup and restore it on the new host ([HOSTING.md "The database"](HOSTING.md#the-database)).

## Before you start: have these ready

### 1. Google sign-in, for treasurers and leaders (about 10 minutes)

Admins sign in with a Google account. You create the "app" that lets them, once:

1. Go to [console.cloud.google.com](https://console.cloud.google.com), create a project (e.g.
   "Troop 52 Finance").
2. **APIs & Services → OAuth consent screen**: choose **External**, fill in the app name and
   your email, and add yourself as a test user (or publish it).
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**: type **Web
   application**. Leave the redirect URI for now; you'll add it in [step 4](#4-finish-up).
4. Keep the **Client ID** and **Client secret**.

(Microsoft sign-in works too, instead or as well; see
[DEPLOY.md step 2](DEPLOY.md#2-register-the-sign-in-apps-admintreasurer-login). Its settings are
`AzureAd__ClientId` and `AzureAd__ClientSecret`.)

### 2. An email service, for parents' sign-in codes (about 10 minutes)

Parents sign in with a 6-digit code sent by email, so the site needs a way to send mail.
[Resend](https://resend.com) and [Brevo](https://www.brevo.com) both have free plans that are
plenty for a troop.

1. Sign up, and add your troop's domain (e.g. `troop52.org`). The service shows a few DNS
   records to add where the domain is managed; without them, mail lands in spam.
2. Keep the **SMTP host**, **username** and **password** (for Resend: `smtp.resend.com`,
   username `resend`, and an API key as the password).
3. Choose the **sender address**, e.g. `noreply@troop52.org`.

No domain of your own? A free plan's shared or sandbox address works for trying it out, but
not reliably for parents.

### What the forms ask for

Every host asks for the same things, under these names or close to them:

| Setting | What to enter |
|---|---|
| `Troop__Name` | `Troop 52`. Shown on every page, email and PDF. |
| `Troop__ContactEmail` | Where families send questions, usually the treasurer |
| `Troop__TimeZone` | Where the troop is: `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix`, `America/Los_Angeles`, `America/Anchorage` or `Pacific/Honolulu` |
| `Auth__GlobalAdmins__0` | Your email: the person looking after the site |
| `Auth__BootstrapEditors__0` | The first Editor's email (the treasurer, or you). They can add everyone else. |
| `Auth__LoginCodePepper` | A long random string, when a host doesn't make one for you (`openssl rand -base64 48`, or 40+ characters from a password manager). **Keep it forever**: changing it unlinks every guardian's authenticator app. |
| `Authentication__Google__ClientId`, `...ClientSecret` | From step 1 |
| `Email__SenderAddress`, `Email__Smtp__Host`, `...Username`, `...Password` | From step 2 |

## 3. Deploy

### Render

Click **Deploy to Render** in the [README](../README.md#set-it-up-for-your-troop). Sign in or
create a Render account, fill in the form (above), and click **Apply**. The first build takes
about 10 minutes. When it's done the app's page shows its address, like
`https://troop-finances-abcd.onrender.com`.

What it makes ([render.yaml](../render.yaml)): the app (Starter, $7), a PostgreSQL database
(Basic, $6), and a 1 GB disk for receipts and backups. Render's free plans can't be used: they
have no disks, and free databases expire.

Settings later: Render → the app → **Environment**.

### Railway

Click **Deploy on Railway** in the [README](../README.md#set-it-up-for-your-troop). Sign in to
Railway (the Hobby plan, $5/month, includes $5 of use, which a troop's site mostly fits in), fill
in the variables it asks for (above), and click **Deploy**. The first build takes about 10
minutes; the app's **Settings → Networking** shows its address, like
`https://troop-finances-production.up.railway.app`.

What it makes: the app from the repository ([railway.json](../railway.json)), a PostgreSQL
database, and a volume for receipts and backups. `Auth__LoginCodePepper` is generated for you.

Settings later: Railway → the app → **Variables**.

### DigitalOcean

App Platform has no disks that survive a redeploy, so first make two **private** Spaces buckets
(DigitalOcean → **Spaces Object Storage**, $5/month for both): one for receipts, one for backups,
e.g. `troop52-receipts` and `troop52-backups`, in the same region. Then **Spaces Object Storage →
Access Keys → Create access key**, and keep the key and secret.

Click **Deploy to DigitalOcean** in the [README](../README.md#set-it-up-for-your-troop). It asks
for each setting with an example filled in: **replace every one**, including
`Storage__S3__ServiceUrl` (your buckets' region, e.g. `https://sfo3.digitaloceanspaces.com`), the
bucket names, and the access key and secret (the same key for both buckets is fine). Leave
`Troop__SiteUrl` as `${APP_URL}` and `ConnectionStrings__FinanceDb` as `${db.DATABASE_URL}`.

What it makes ([.do/deploy.template.yaml](../.do/deploy.template.yaml)): the app (1 GB, $12) and a
PostgreSQL dev database ($7).

If the first start fails with "permission denied for schema public", the dev database wasn't
finished being set up when the app first connected: DigitalOcean's fix is to delete the database
component, deploy, add a dev database again, and deploy once more.

Settings later: the app → **Settings → App-Level / Component Environment Variables**.

### Azure

Click **Deploy to Azure** in the [README](../README.md#set-it-up-for-your-troop). Sign in to the
Azure portal, choose a subscription and a **new** resource group (e.g. `troop52-finance`), fill in
the form (above, with friendlier names), and click **Review + create**. Leave the three generated
values at the bottom as they are. It takes about 10 minutes; the deployment's **Outputs** show the
app's address and the redirect addresses for step 4.

What it makes ([infra/container-apps](container-apps/main.bicep)): the app as an Azure Container
App that stops when nobody is using it, a PostgreSQL flexible server (B1ms, about $13 + $4 for
its disk; free for 12 months on a new Azure account), a storage account for receipts, a second
one for backups (copied to a second region), and a small nightly job that starts the backup.

**Don't run the template again on the same resource group** to change a setting: it would make a
new `Auth__LoginCodePepper` and unlink guardians' authenticator apps. Change settings in the
portal instead: the Container App → **Containers → Environment variables** (or **Secrets**).

<details>
<summary>The same from a terminal, with the Azure Developer CLI</summary>

```bash
git clone https://github.com/sacparker09/troop-finances && cd troop-finances
azd auth login
azd env new troop52-finance                 # becomes the resource names: 3-20 lowercase characters
azd env set LOGIN_CODE_PEPPER "$(openssl rand -base64 48)"
azd env set DATABASE_PASSWORD "Tf-$(openssl rand -hex 16)"
azd env set BACKUP_TRIGGER_KEY "$(openssl rand -hex 32)"
azd env set GOOGLE_CLIENT_ID "<from step 1>"
azd env set GOOGLE_CLIENT_SECRET "<from step 1>"
azd up                                      # asks for the rest, then creates everything
```

azd keeps these values (in `.azure/troop52-finance/`), so running `azd up` again later is safe.
Keep that folder, or the values in it, somewhere safe.
</details>

### Fly.io

You need the [`fly` command](https://fly.io/docs/flyctl/install/) and a GitHub account.

1. **A database.** Create a free project at [Neon](https://neon.tech) in the region nearest your
   Fly region, and copy its connection string, the **direct** one (not "pooled").
2. **Your own copy of the repository.** Fork it on GitHub (the nightly backup runs from there),
   then clone your fork.
3. **Create the app**, from the repository's folder:

   ```bash
   fly auth login
   fly launch --copy-config --no-deploy --name troop52-finance --region ord
   ```

   Pick a name of your own (the site becomes `https://<name>.fly.dev`) and the
   [region](https://fly.io/docs/reference/regions/) nearest the troop.
4. **Settings** (above), as secrets. First make the backup key, and keep it for step 6
   (`fly secrets` can't show it again):

   ```bash
   BACKUP_KEY=$(openssl rand -hex 32); echo "$BACKUP_KEY"
   fly secrets set \
     Troop__Name="Troop 52" Troop__ContactEmail="treasurer@troop52.org" Troop__TimeZone="America/Chicago" \
     Auth__GlobalAdmins__0="you@example.org" Auth__BootstrapEditors__0="treasurer@troop52.org" \
     Auth__LoginCodePepper="$(openssl rand -base64 48)" \
     Backup__TriggerKey="$BACKUP_KEY" \
     Authentication__Google__ClientId="..." Authentication__Google__ClientSecret="..." \
     Email__SenderAddress="noreply@troop52.org" Email__Smtp__Host="smtp.resend.com" \
     Email__Smtp__Username="resend" Email__Smtp__Password="..." \
     ConnectionStrings__FinanceDb="postgresql://...from Neon..."
   ```

5. **Deploy:** `fly deploy --ha=false` (one copy only; see
   [HOSTING.md](HOSTING.md#one-copy-at-a-time)). The first deploy also creates the 1 GB volume.
6. **The nightly backup.** The app stops when nobody is using it, so the backup is started from
   your fork's GitHub Actions: in your fork, **Settings → Secrets and variables → Actions**, add the
   variable `BACKUP_TRIGGER_URL` = `https://<name>.fly.dev/internal/backup` and the secret
   `BACKUP_TRIGGER_KEY` = the key from step 4. Then **Actions** → enable workflows.

What it makes ([fly.toml](../fly.toml)): the app (1 GB, billed only while it runs) and a 1 GB
volume for receipts and backups. Neon's free database sleeps when idle too.

Settings later: `fly secrets set Name=value` (the app restarts).

### Your own server

Any Linux server with [Docker](https://docs.docker.com/engine/install/) and 2 GB of memory: a
$6-12/month VPS (DigitalOcean, Hetzner, Linode, Lightsail), or a computer at home with ports 80
and 443 forwarded to it.

1. Point a domain at it: a DNS **A record** for e.g. `finance.troop52.org` with the server's IP
   address.
2. On the server:

   ```bash
   git clone https://github.com/sacparker09/troop-finances && cd troop-finances
   cp .env.prod.example .env.prod
   nano .env.prod                  # fill it in: the settings above, plus two random passwords
   docker compose -f docker-compose.prod.yml --env-file .env.prod up -d
   ```

What it runs ([docker-compose.prod.yml](../docker-compose.prod.yml)): the app (the published
image), PostgreSQL, and Caddy, which gets and renews an HTTPS certificate by itself. Backups run
every night inside the app; send them to a bucket somewhere else (the `BACKUP_S3_*` settings),
because a backup on the same server goes if the server does.

You look after the server: its updates, its disk, and copies of the database beyond the app's
own nightly backup. On Coolify or Dokploy, deploy `docker-compose.prod.yml` from the repository
and leave out its `caddy` service; they put their own HTTPS in front.

Settings later: edit `.env.prod`, then run the `up -d` command again.

## 4. Finish up

1. **Sign-in redirect:** back in the Google console (step 1), edit the OAuth client and add the
   redirect URI `https://<your address>/signin-google`. (Microsoft:
   `https://<your address>/signin-oidc` on the app registration.)
2. **Sign in** at `https://<your address>/admin/signin` with the Editor's Google account.
3. **Check email:** send yourself a guardian sign-in code at `/portal/login`.
4. **Check backups:** Settings → Backups → **Run backup now**, and confirm it completes.
5. **Add scouts and guardians** under Families, and the troop's bank accounts under Bank.
6. **Read the Terms and Privacy pages** (`/terms`, `/privacy`). They use your troop's name and
   contact, but the wording was written for one troop and mentions Azure; see
   [HOSTING.md](HOSTING.md#your-troops-settings).
7. Optional, in the host's settings:
   - `Troop__CharteredOrganization` and `Troop__Ein`, for donation thank-you letters.
   - `Troop__SiteUrl`, once you give the site its own domain (in the host's custom-domain
     settings), e.g. `https://finance.troop52.org/`. Add the new address's redirect URIs too.

## Backups

Every night the app saves a copy of all records and receipts, and keeps the newest 14.

| Host | Where the copies go |
|---|---|
| Render, Railway, Fly.io, your own server | The same disk as the receipts. This covers mistakes (a deleted record, a bad import) but not losing the disk. |
| DigitalOcean | The backups bucket. |
| Azure | A separate storage account, copied to a second region. |

To keep the nightly copies somewhere else as well, create a bucket at Cloudflare R2 or
Backblaze B2 (both have free tiers) and change the `BackupStorage__*` settings as described in
[HOSTING.md "Backups"](HOSTING.md#backups-backupstorage__provider). Render and DigitalOcean also
back up their databases themselves; Azure keeps 7 days of its own.

## Updates

| Host | How |
|---|---|
| Render | Rebuilds whenever the repository it was created from changes. From your own fork: **Sync fork** on GitHub. |
| Railway | Shows when the template's repository has changed; or connect the service to your own fork and **Sync fork**. |
| DigitalOcean | The app → **Deploy** (it builds the latest from the repository). |
| Azure | `az containerapp update -g <resource group> -n <app name> --image ghcr.io/sacparker09/troop-finances:latest --revision-suffix u$(date +%s)` |
| Fly.io | `git pull` (or **Sync fork**), then `fly deploy --ha=false`. |
| Your own server | `docker compose -f docker-compose.prod.yml --env-file .env.prod pull`, then `... up -d`. |

The app updates its own database tables when it starts.

## Try it first: GitHub Codespaces

Click **Open in GitHub Codespaces** in the [README](../README.md#set-it-up-for-your-troop). In a
few minutes the app opens in a browser tab, as a sample "Troop 100" with test data. Sign in as an
admin with the fake sign-in it offers; guardians' sign-in codes appear in Mailpit (the
**Ports** tab, port 8025). Codespaces are free for a few hours a month on a personal GitHub
account; stop it from [github.com/codespaces](https://github.com/codespaces) when you're done.

It's for trying things out only: never put a troop's real records in it.

## If something goes wrong

Look at the app's logs (Render/Railway/DigitalOcean: the app's **Logs**; Azure: the Container App
→ **Log stream**; Fly: `fly logs`; your own server: `docker compose -f docker-compose.prod.yml
logs app`). A site that won't start says why on the first lines, e.g. "This site isn't set up for
a troop yet: Troop:Name isn't set". A message mentioning `/app/data` and "Permission denied"
means the disk isn't writable by the app: on Railway, check the variable `RAILWAY_RUN_UID` is
`0`; elsewhere, see [HOSTING.md](HOSTING.md) for using an S3-compatible bucket for receipts
instead.
