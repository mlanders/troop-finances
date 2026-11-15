# Deploying to Azure

The Bicep template (`main.bicep`) provisions the Storage Account (Table + Blob), the App Service,
and the Azure Communication Services Email resources. It does **not** cover:

- The Microsoft app registration and Google OAuth client for the treasurer/admin sign-in --
  neither is an Azure resource, so Bicep/ARM can't create them (same reason as the sibling
  meeting sign-in app).
- DNS verification for the `noreply@<your-domain>` sender domain -- Bicep creates the `Domain`
  resource, but can't touch DNS records outside Azure.
- The `Auth:LoginCodePepper` secret (the HMAC key used to hash login codes before storing them)
  -- generate your own and set it as an app setting, never commit it. It also derives the key
  that encrypts guardians' authenticator-app secrets, so **changing it unlinks every
  authenticator app** (guardians can still sign in by email and link theirs again).

The commands below use placeholders for your own Azure names: `<resource-group>`, `<web-app>`
(the App Service's name), `<sql-server>`, `<storage-account>`, `<backup-storage-account>`,
`<acs-resource>` and `<your-domain>`. To share an App Service plan with another app, set
`useExistingAppServicePlan` and the `existingAppServicePlan*` parameters.

## 1. Deploy the Azure resources

Your deployment's names and parameters stay out of git, in `infra/local/` (ignored). Once:

```bash
mkdir -p infra/local
cp infra/main.parameters.example.json infra/local/main.parameters.json   # then fill it in
```

Then:

```bash
az login
az deployment group create \
  --resource-group <resource-group> \
  --template-file infra/main.bicep \
  --parameters infra/local/main.parameters.json
```

For `infra/deploy.ps1` (step 5), also create `infra/local/deploy.json`:
`{ "resourceGroup": "<resource-group>", "webAppName": "<web-app>" }`.

Note the `webAppUrl` and `signInRedirectUri` outputs -- you'll need them next.

## 2. Register the sign-in apps (admin/treasurer login)

Treasurers sign in at `/admin/signin` with a Microsoft account (work, school or personal) or a
Google account, like the meal-planner app. Each provider's button only appears once its client id
is set, so either can be left out. Signing in grants nothing by itself: access is given in the app
(see "Who gets access" below).

### Microsoft

Recommend a **separate** app registration from the meeting sign-in app's -- different redirect
URIs, different blast radius for an app that handles financial data.

```bash
appId=$(az ad app create \
  --display-name "<Troop name> Finances" \
  --sign-in-audience AzureADandPersonalMicrosoftAccount \
  --web-redirect-uris "https://<webAppUrl-from-step-1>/signin-oidc" "https://localhost:7255/signin-oidc" \
  --enable-id-token-issuance true \
  --query appId -o tsv)

echo "Client ID: $appId"

# Personal Microsoft accounts need v2 tokens.
az ad app update --id "$appId" --set api.requestedAccessTokenVersion=2

az ad app credential reset --id "$appId" --append --query password -o tsv
```

An existing registration created for the old tenant-only sign-in can be switched instead:

```bash
az ad app update --id "<client-id>" --set api.requestedAccessTokenVersion=2
az ad app update --id "<client-id>" --sign-in-audience AzureADandPersonalMicrosoftAccount
```

It no longer needs `groupMembershipClaims` or the `GroupMember.Read.All` Graph permission; both
can be removed. Switch the registration **before** setting `AzureAd__TenantId=common` (step 4):
a tenant-only registration rejects sign-ins through the `common` endpoint.

### Google

In the Google Cloud console, under APIs & Services > Credentials, create an OAuth client ID of
type **Web application**:
- Authorized redirect URIs: `https://<webAppUrl-from-step-1>/signin-google` and
  `https://localhost:7255/signin-google`.
- The consent screen only needs the default `openid`, `email` and `profile` scopes.

Keep the client ID and client secret for step 4.

### Who gets access

The first time someone signs in, they see a "contact an administrator" page and show up as an
access request under **Settings > People & access**. An Editor then gives them Read-only or Edit
access there.

The first Editor comes from the `Auth:BootstrapEditors` setting (set in step 4). While nobody has Edit access yet, a listed address that signs in becomes
an Editor. It must be an address the provider vouches for: a Google-verified email, or a
Microsoft account's sign-in name. After that first Editor exists, the setting does nothing.

**Global admin (the system admin):** the `Auth:GlobalAdmins` setting (set in step 4; the site
won't start without one) lists the Global admins. A Global admin is Read only day to day but can turn
on Edit for themselves when something needs fixing. They can also allow other Read-only people to
do the same. Once the treasurer has Edit access, ask them to change the Global admin to **Read
only** under **Settings > People & access**. Until then, the Global admin stays a standing Editor.

To turn on Edit, click **View only · Turn on edit** in the header, give a reason, and pick 1, 2, 4
or 8 hours. Each time emails every standing Editor, the committee chair and the
`Auth:EditorGrantNotify` addresses, and is recorded in Activity History. It ends on its own, and
while it's on it can't change who has access. There's no switch to turn the alert off.

**Moving over from the Entra groups:** the old "Troop Finance Writer"/"Troop Finance Reader"
security groups are no longer used. After this version is deployed:
1. Sign in with a bootstrap address.
2. Ask everyone who had access to sign in once.
3. Give each of them their role from the access-request list.

## 3. Verify the email sending domain

The Bicep deploy created a `Domain` resource for `<your-domain>` with `CustomerManaged` domain
management -- this needs manual DNS verification before `noreply@<your-domain>` can actually send:

1. In the Azure Portal, open the `<baseName>-email` Email Communication Service resource →
   Domains → `<your-domain>`. It lists the exact records to add: one TXT (domain ownership), one
   TXT (SPF), and two CNAME (DKIM).
2. Add those exact records at <your-domain>'s DNS provider. **You'll need access to that domain's
   DNS management to do this.**
3. Once DNS has propagated (can take up to 24-48 hours, though usually much faster), click
   "Verify" on the domain in the Portal.

Until verification completes, login-code emails (and the optional "Edit access granted" email)
will fail to send. The admin/treasurer side and everything else in the app work independently of
this.

## 4. Wire secrets into the deployed app

```bash
# Generate a random pepper for hashing login codes (any long random string works).
pepper=$(openssl rand -base64 32)

az webapp config appsettings set \
  --resource-group <resource-group> \
  --name <web-app> \
  --settings \
    AzureAd__TenantId="common" \
    AzureAd__ClientId="$appId" \
    AzureAd__ClientSecret="<the secret from step 2>" \
    Authentication__Google__ClientId="<google-client-id>" \
    Authentication__Google__ClientSecret="<google-client-secret>" \
    Auth__LoginCodePepper="$pepper"
```

`AzureAd__TenantId="common"` accepts any Microsoft account. To allow only the troop's own tenant,
set the tenant id instead, and register the app as `AzureADMyOrg`.

The Bicep template intentionally does not manage App Service app settings. This prevents a later
infrastructure deployment from deleting the sign-in client secrets or login-code pepper. On a new
environment, add the non-secret resource settings along with the authentication settings:

```bash
storageConnection=$(az storage account show-connection-string \
  --resource-group <resource-group> --name <storage-account> --query connectionString -o tsv)
acsConnection=$(az communication list-key \
  --resource-group <resource-group> --name <acs-resource> \
  --query primaryConnectionString -o tsv)

az webapp config appsettings set \
  --resource-group <resource-group> \
  --name <web-app> \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__AzureStorage="$storageConnection" \
    ConnectionStrings__AzureCommunicationServices="$acsConnection" \
    Email__SenderAddress="noreply@<your-domain>" \
    AzureAd__Instance="https://login.microsoftonline.com/" \
    AzureAd__TenantId="common" \
    AzureAd__ClientId="<client-id>" \
    AzureAd__ClientSecret="<client-secret>" \
    AzureAd__CallbackPath="/signin-oidc" \
    Authentication__Google__ClientId="<google-client-id>" \
    Authentication__Google__ClientSecret="<google-client-secret>" \
    Auth__LoginCodePepper="<long-random-secret>"
```

Do not commit or print the client secrets or pepper.

**The troop's own settings** (required; the site won't start without them -- see
[HOSTING.md "Your troop's settings"](HOSTING.md#your-troops-settings) for what each does), and who
administers it:

```powershell
az webapp config appsettings set --resource-group <resource-group> --name <web-app> --settings `
  "Troop__Name=<Troop 52>" "Troop__ShortName=<52 Finance>" "Troop__AuthenticatorName=<T52 Finance Guardian>" `
  "Troop__SiteUrl=https://finance.<your-domain>/" "Troop__ContactEmail=<treasurer email>" `
  "Troop__TimeZone=<America/Chicago>" "Email__SenderAddress=noreply@<your-domain>" `
  "Auth__GlobalAdmins__0=<global admin's email>" "Auth__BootstrapEditors__0=<first Editor's email>" `
  "Auth__EditorGrantNotify__0=<address told when Edit access is given>"
```

Optional: donation thank-you letters (Donations → a gift → "Thank-you letter") name the troop's
chartered organization and its EIN. Set them once and every letter is filled in; until then the
letter shows highlighted `[Chartered organization]` / `[EIN]` placeholders to type over in Word.

```powershell
az webapp config appsettings set --resource-group <resource-group> --name <web-app> --settings "Troop__CharteredOrganization=<chartered organization name>" "Troop__Ein=<12-3456789>"
```

**Security note**: unlike some early history in the sibling sign-in app, keep all of the above out
of anything committed to source control -- set them only via `az webapp config appsettings set`
(as above) or `dotnet user-secrets` locally, never in `appsettings.json`/`main.parameters.json`.

## 5. Deploy the code

```powershell
.\infra\deploy.ps1
```

(Override `-ResourceGroup`/`-WebAppName` if you changed those from the defaults.)

## 6. Verify

- `https://<webAppUrl>/` -- landing page loads, no login prompt.
- `https://<webAppUrl>/admin` -- redirects to Microsoft sign-in, lands on the dashboard.
- `https://<webAppUrl>/portal/login` -- enter a pre-registered guardian's email (add one via
  `/admin/guardians` first); once the domain is verified (step 3), a real code should arrive.

### Sign-in keys (once, after the November 2026 update)

The keys that sign the admin and guardian sign-in cookies now live in the SQL database
(`DataProtectionKeys` table) rather than on the App Service's disk, so the app can also run on
other hosts ([HOSTING.md](HOSTING.md)). The first deploy with that change starts a fresh set of keys, which
**signs everyone out once**: admins sign in again, and guardians need a new emailed code. Nothing
else is affected (authenticator apps don't use these keys). Deploy in the evening, or tell
guardians to expect it.

## 7. Backups and restore

Every night after 2:00 AM Pacific the app exports every table of the SQL database (except login
codes) and copies every receipt into the separate, delete-locked `<backup-storage-account>` storage
account, under `backups/{yyyy-MM-dd}/`. It keeps the newest 14 complete backups and deletes older
ones, but only after a backup succeeds, so if backups break, the last 14 good ones stay put. A
60-day lifecycle rule on that account is only a backstop, and Azure SQL's own point-in-time
restore is a second, independent safety net. Status and a "Run backup now" button are on
**Settings → Backups** in the app. The code is in `Services/BackupService.cs`.

### One-time setup

After deploying `main.bicep` (step 1 creates the backup account), give the app its connection
string:

```bash
backupConn=$(az storage account show-connection-string \
  --resource-group <resource-group> --name <backup-storage-account> --query connectionString -o tsv)
az webapp config appsettings set --resource-group <resource-group> --name <web-app> \
  --settings ConnectionStrings__BackupStorage="$backupConn"
```

Without that setting, backups stay off: the app logs a warning at startup and Settings →
Backups says so.

### Restoring

Restore runs from your own machine with the app's command-line mode. It shows what's in the
backup and makes you type the date before it writes anything. All the rows are restored in one
transaction, so a restore lands completely or not at all.

1. **Practice on a scratch database first.** Restore into LocalDB and check the data looks right:
   ```powershell
   cd src\BsaTroopFinances
   $env:ConnectionStrings__BackupStorage = "<backup account connection string>"
   dotnet run -- restore-backup 2026-10-05 --replace-all `
     --target "Server=(localdb)\MSSQLLocalDB;Database=RestoreCheck;Trusted_Connection=True;TrustServerCertificate=True" `
     --receipts-target "UseDevelopmentStorage=true"
   ```
2. **Restore production.** Point `--target` at the Azure SQL database and `--receipts-target` at
   the live storage account:
   ```powershell
   dotnet run -- restore-backup 2026-10-05 --target "<Azure SQL connection string>" `
     --receipts-target "<live storage account connection string>"
   ```
   (For a PostgreSQL database, also set `$env:Database__Provider = "Postgres"` and give its
   connection string to `--target`; see [HOSTING.md](HOSTING.md#the-database).)
   - Default mode is **upsert**: rows in the backup overwrite matching rows, and anything added
     since is left alone. Use it to bring back deleted or damaged records.
   - Add **`--replace-all`** to first delete every row in each backed-up table, so the tables
     match the backup exactly. Use it to roll everything back to that night.
   - Receipts are copied back either way. Receipts uploaded since that backup aren't removed.

Stop the web app (`az webapp stop`) during a `--replace-all` restore so nobody writes data
halfway through, then start it again.

Backups taken **before the move to SQL** hold Table Storage tables instead; `restore-backup`
refuses them. The `migrate-to-sql` command that could load them has been removed (step 8).

## 8. Moving the data from Table Storage to Azure SQL (one-time)

> **Status: done.** Cut over on 2026-10-08 (site down 4:33–4:46 PM Pacific). The migration
> report passed 86/86 checks; the live app runs on Azure SQL. Table Storage is untouched as the
> rollback copy until 2026-11-07 (step 5 below).

1. **Create the database** (done): `main.bicep` creates the server (Microsoft Entra sign-in only,
   no SQL password; the administrator is set by `sqlEntraAdminLogin`/`sqlEntraAdminObjectId`),
   the Basic database, a virtual network rule for the web app's subnet (step 9) and a delete
   lock. Then, signed in as
   that administrator, a one-time script made the web app's managed identity a database user:
   ```sql
   CREATE USER [<web-app>] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [<web-app>];
   ALTER ROLE db_datawriter ADD MEMBER [<web-app>];
   ALTER ROLE db_ddladmin ADD MEMBER [<web-app>];
   ```
   and the web app got `ConnectionStrings__FinanceDb` =
   `Server=tcp:<sql-server>.database.windows.net,1433;Database=troopfinances;Authentication=Active Directory Managed Identity;Encrypt=True;`.
   Running a script from your own machine needs a firewall rule for your IP
   (`az sql server firewall-rule create ... -n setup-dev-machine`) and a connection string with
   `Authentication=Active Directory Default`, which signs in through `az login`. Remove the rule
   when you're done. The server's `finance-sql-no-delete` lock also blocks deleting firewall
   rules, so lift it, delete the rule, and put it back:
   ```bash
   az lock delete --name finance-sql-no-delete -g <resource-group> --resource <sql-server> --resource-type Microsoft.Sql/servers
   az sql server firewall-rule delete -g <resource-group> -s <sql-server> -n setup-dev-machine
   az lock create --name finance-sql-no-delete --lock-type CanNotDelete -g <resource-group> --resource <sql-server> --resource-type Microsoft.Sql/servers
   ```
2. **Rehearse and cut over** (done): a `migrate-to-sql` command copied everything in one
   transaction and checked row counts, every scout's balance, totals, and sample rows field by
   field. It was rehearsed against a nightly backup, then run against the live tables with the
   site stopped.
3. **Clean up:** `migrate-to-sql` (`Data/TableStorageImport`) and the `Azure.Data.Tables`
   package have been removed; they're in git history if ever needed. Rolling back still means
   redeploying a pre-SQL build, and anything entered since cutover would have to be re-entered.
   After the rollback window ends on 2026-11-07, delete the old Table Storage tables from the
   storage account. The receipt blob containers in the same account stay.

## 9. Who can reach the SQL server

The SQL server accepts connections from one place: the web app's subnet (`app-integration` in the
`troop-finances-vnet` virtual network), through a `Microsoft.Sql` service endpoint and the
`allow-app-subnet` virtual network rule. There is no "allow Azure services" firewall rule, which
would let any Azure tenant attempt a connection. Sign-in is still Entra-only on top of that.
Your own machine needs a temporary IP rule (step 8.1) for restores and one-time scripts.

### Switching an existing server over (one-time)

`main.bicep` no longer declares the old `AllowAllWindowsAzureIps` rule, but a deployment never
deletes what it stops declaring, so remove it by hand after the new path is proven:

1. Deploy the template (step 1). This creates the network, adds the SQL rule, and joins the web
   app to the subnet, which restarts the site (under a minute).
2. Check the site works: open `/admin`, then **Settings → Backups → Run backup now**.
3. Delete the old rule (the server's delete lock blocks this, so lift it and put it back):
   ```bash
   az lock delete --name finance-sql-no-delete -g <resource-group> --resource <sql-server> --resource-type Microsoft.Sql/servers
   az sql server firewall-rule delete -g <resource-group> -s <sql-server> -n AllowAllWindowsAzureIps
   az lock create --name finance-sql-no-delete --lock-type CanNotDelete -g <resource-group> --resource <sql-server> --resource-type Microsoft.Sql/servers
   ```
4. Check the site again. This is the real test: the app now has only the subnet path. If it
   can't reach the database, put the rule back while you investigate:
   ```bash
   az sql server firewall-rule create -g <resource-group> -s <sql-server> -n AllowAllWindowsAzureIps --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
   ```
