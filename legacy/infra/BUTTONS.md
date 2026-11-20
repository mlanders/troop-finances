# Maintaining the deploy buttons

For whoever looks after this repository: what each setup option in [SETUP.md](SETUP.md) is made
of, the one-time steps that can't live in the repository, and what to change in a fork that
wants buttons of its own.

## What each one uses

| Option | Files | Builds from | Needs, once |
|---|---|---|---|
| Render | [render.yaml](../render.yaml) | The repository | Nothing |
| Railway | [railway.json](../railway.json) | The repository | A published template ([below](#railway-publish-the-template)) |
| DigitalOcean | [.do/deploy.template.yaml](../.do/deploy.template.yaml) | The repository | Nothing (the repository must be public) |
| Azure (button and `azd up`) | [infra/container-apps/](container-apps/main.bicep), [azure.yaml](../azure.yaml) | The published image | A public image ([below](#the-published-image)) |
| Fly.io | [fly.toml](../fly.toml) | The person's clone | Nothing |
| Your own server | [docker-compose.prod.yml](../docker-compose.prod.yml), [.env.prod.example](../.env.prod.example) | The published image | A public image |
| Codespaces | [.devcontainer/devcontainer.json](../.devcontainer/devcontainer.json) | The repository | Nothing |

All of them run the same Dockerfile with PostgreSQL and the settings in
[HOSTING.md](HOSTING.md). A new required setting has to be added to each file above, and to the
table in SETUP.md "What the forms ask for".

## The published image

[.github/workflows/ci.yml](../.github/workflows/ci.yml) runs the tests on every push, and once
they pass on `main` builds the Dockerfile and publishes `ghcr.io/<owner>/troop-finances` (`latest`,
`sha-<commit>`, and `vX.Y.Z` for a `vX.Y.Z` tag).

**Once, after its first run:** GitHub → your profile → **Packages** → `troop-finances` →
**Package settings** → **Change visibility** → **Public**. Until then the Azure button and
`docker-compose.prod.yml` fail with "unauthorized" or "manifest unknown" for everyone else.

## Azure: keep azuredeploy.json in step

The button deploys `infra/container-apps/azuredeploy.json`, the compiled form of `main.bicep`.
After any change to `main.bicep`:

```bash
az bicep build --file infra/container-apps/main.bicep --outfile infra/container-apps/azuredeploy.json
```

and commit both. The button URL is the raw GitHub address of that file, URL-encoded:
`https://portal.azure.com/#create/Microsoft.Template/uri/` +
`https%3A%2F%2Fraw.githubusercontent.com%2F<owner>%2Ftroop-finances%2Fmain%2Finfra%2Fcontainer-apps%2Fazuredeploy.json`.

`azd up` uses `main.bicep` directly, with `main.parameters.json` taking the generated secrets from
azd's environment (so they stay the same on every `azd up`) instead of the template's `newGuid()`
defaults, which would change them each time.

## Railway: publish the template

Railway's button opens a template, which lives in Railway's dashboard, not the repository. Once:

1. In Railway, **New Project → Deploy from GitHub repo** → this repository. It builds with
   `railway.json` (the Dockerfile, health check `/healthz`, one replica).
2. **+ New → Database → PostgreSQL** in the same project.
3. On the app service: **Settings → Volumes → Add volume**, mounted at `/app/data`, and
   **Settings → Networking → Generate domain**.
4. On the app service, **Variables**, add:

   | Variable | Value |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `PORT` | `8080` |
   | `RAILWAY_RUN_UID` | `0` (Railway's volumes belong to root; this lets the app write to it) |
   | `Database__Provider` | `Postgres` |
   | `ConnectionStrings__FinanceDb` | `${{Postgres.DATABASE_URL}}` |
   | `Auth__LoginCodePepper` | `${{secret(64)}}` |
   | `Storage__Provider` / `BackupStorage__Provider` | `FileSystem` |
   | `Storage__FileSystem__Root` | `/app/data/files` |
   | `BackupStorage__FileSystem__Root` | `/app/data/backups` |
   | `Email__Provider` | `Smtp` |
   | `Email__Smtp__Port` | `587` |
   | `Troop__Name`, `Troop__ContactEmail`, `Troop__TimeZone`, `Auth__GlobalAdmins__0`, `Auth__BootstrapEditors__0`, `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret`, `Email__SenderAddress`, `Email__Smtp__Host`, `Email__Smtp__Username`, `Email__Smtp__Password` | Empty: whoever deploys fills these in. Give each a description from SETUP.md "What the forms ask for". |

   `Troop__SiteUrl` isn't needed: the app takes its address from Railway's
   `RAILWAY_PUBLIC_DOMAIN`.
5. Project **Settings → Generate template from project** (or **Templates → New template**),
   check the variables, and **Publish**.
6. In the README, uncomment the Railway button and put the template's code (the end of its
   `railway.com/deploy/...` address) in place of `TEMPLATE_CODE`.

Changing the template later is done in Railway's template editor, not in the repository.

## DigitalOcean

The `repo_clone_url` in `.do/deploy.template.yaml` must be the same repository the button links to.
The button only offers a dev database (not a managed one), and only for a public repository.
Validate a change by deploying it from a test account; `doctl apps spec validate` doesn't
understand the template's `spec:` wrapper.

## In a fork

Replace `sacparker09` with your GitHub name in: the README buttons, `.do/deploy.template.yaml`,
the `containerImage` default in `infra/container-apps/main.bicep` (then rebuild
`azuredeploy.json`), `docker-compose.prod.yml`, `.env.prod.example`, `azure.yaml`'s comment, and
the commands in SETUP.md. Then make your image public and publish your own Railway template as
above.
