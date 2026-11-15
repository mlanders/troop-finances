# Set it up for your troop

This puts your own copy of the app online, with its own database, on [Render](https://render.com).
Your troop's records stay in your copy; nobody else's site can see them. Plan on about 30
minutes, most of it in steps 1 and 2.

**Cost:** about $13 a month, billed by Render to whoever creates the account (about $7 for the
app, $6 for the database, and a few cents for file storage). Render's free plans can't be used.

## Before you start: have these ready

### 1. Google sign-in, for treasurers and leaders (about 10 minutes)

Admins sign in with a Google account. You create the "app" that lets them, once:

1. Go to [console.cloud.google.com](https://console.cloud.google.com), create a project (e.g.
   "Troop 52 Finance").
2. **APIs & Services → OAuth consent screen**: choose **External**, fill in the app name and
   your email, and add yourself as a test user (or publish it).
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**: type **Web
   application**. Leave the redirect URI for now; you'll add it after step 3 below.
4. Keep the **Client ID** and **Client secret**.

(Microsoft sign-in works too; see [DEPLOY.md step 2](DEPLOY.md#2-register-the-sign-in-apps-admintreasurer-login).
Add `AzureAd__ClientId` and `AzureAd__ClientSecret` in Render afterwards.)

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

## 3. Deploy

Click **Deploy to Render** in the [README](../README.md#set-it-up-for-your-troop) (or open
`https://render.com/deploy?repo=` followed by this repository's GitHub address). Sign in or
create a Render account, and fill in the form:

| Asked for | What to enter |
|---|---|
| `Troop__Name` | `Troop 52`. Shown on every page, email and PDF. |
| `Troop__ContactEmail` | Where families send questions, usually the treasurer |
| `Troop__TimeZone` | Where the troop is: `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix`, `America/Los_Angeles`, `America/Anchorage` or `Pacific/Honolulu` |
| `Auth__GlobalAdmins__0` | Your email: the person looking after the site |
| `Auth__BootstrapEditors__0` | The first Editor's email (the treasurer, or you). They can add everyone else. |
| `Authentication__Google__ClientId`, `...ClientSecret` | From step 1 |
| `Email__SenderAddress`, `Email__Smtp__Host`, `...Username`, `...Password` | From step 2 |

Click **Apply**. The first build takes about 10 minutes. When it's done the app's page shows its
address, like `https://troop-finances-abcd.onrender.com`.

## 4. Finish up

1. **Google sign-in redirect:** back in the Google console (step 1), edit the OAuth client and
   add the redirect URI `https://<your address>/signin-google`.
2. **Sign in** at `https://<your address>/admin/signin` with the Editor's Google account.
3. **Add scouts and guardians** under Families, and the troop's bank accounts under Bank.
4. **Read the Terms and Privacy pages** (`/terms`, `/privacy`). They use your troop's name and
   contact, but the wording was written for one troop and mentions Azure; see
   [HOSTING.md](HOSTING.md#your-troops-settings).
5. Optional, in Render → the app → **Environment**:
   - `Troop__CharteredOrganization` and `Troop__Ein`, for donation thank-you letters.
   - `Troop__SiteUrl`, once you give the site its own domain (Render → **Settings → Custom
     Domains**), e.g. `https://finance.troop52.org/`.

## Backups

Every night the app saves a copy of all records and receipts. On this setup the copies are on
the same disk as the receipts, which protects against mistakes (a deleted record, a bad import)
but not against losing the disk. Render also backs up the database itself on paid plans.

To keep the nightly copies somewhere else as well, create a bucket at Cloudflare R2 or
Backblaze B2 (both have free tiers) and change the `BackupStorage__*` settings as described in
[HOSTING.md "Backups"](HOSTING.md#backups-backupstorage__provider).

## Updates

Render rebuilds your copy whenever the repository it was created from changes. If you made your
own copy (a fork) on GitHub, use **Sync fork** there to take updates, and Render redeploys.

## If something goes wrong

Render → the app → **Logs**. A site that won't start says why on the first lines, e.g. "This
site isn't set up for a troop yet: Troop:Name isn't set". A message mentioning
`/app/data` and "Permission denied" means the disk isn't writable by the app: see
[HOSTING.md](HOSTING.md) for using an S3-compatible bucket for receipts instead.
