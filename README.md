# Troop Finances

Money tracking for a Scouts BSA troop: reimbursement receipts that adults submit, invoices scouts
owe, and fundraiser proceeds credited to each scout's account. Parents see their scout's balance and
submit receipts from their phone; the treasurer manages the rest.

Written in TypeScript for **Cloudflare Workers** (Hono), with **D1** for data and **R2** for receipt files.
It fits comfortably in Cloudflare's free tier for a troop-sized site.

## One-click install

[![Deploy to Cloudflare](https://deploy.workers.cloudflare.com/button)](https://deploy.workers.cloudflare.com/?url=https://github.com/mlanders/troop-finances)

Cloudflare copies the repo to your GitHub, creates the D1 database and R2 bucket, runs the migrations
and deploys. When it asks for settings:

| Setting | What to enter |
| --- | --- |
| `TROOP_NAME` | e.g. `Troop 123` |
| `ADMIN_EMAILS` | The treasurer's email(s), comma-separated. These can sign in as admins. |
| `EMAIL_FROM` | Sender for sign-in codes, e.g. `Troop 123 <finance@yourdomain.org>` |
| `RESEND_API_KEY` | Secret. Free key from [resend.com](https://resend.com) (verify your sending domain there). |
| `CODE_PEPPER` | Secret. Any long random string. |

Then open your `*.workers.dev` URL, sign in with an admin email, add scouts and guardians, and link them.
Guardians sign in with the same email-code flow using the address you gave for them.

No email service yet? Sign-in codes are printed to the Worker log instead (`npx wrangler tail`), which is enough to get started.

## What it does

- **Scouts & guardians**: bulk add, link families, per-scout ledger and balance.
- **Invoices**: issue to many scouts at once; money already in a scout's account is applied first, and the family owes the rest. Record check/cash/Venmo payments; void with a reason.
- **Fundraisers**: record raised/expenses, then split the net evenly into scouts' accounts (to the cent).
- **Reimbursements**: guardians upload a receipt photo/PDF (stored in R2); the treasurer approves, rejects or marks paid.
- **Audit log** on the dashboard; passwordless email-code sign-in; admin management.

## Develop

```bash
npm install
cp .dev.vars.example .dev.vars   # then edit
npm run db:local
npm run dev                      # http://localhost:8787, sign-in codes print to the terminal
npm test && npm run typecheck
```

Deploy manually with `npx wrangler login && npm run deploy`. The Worker creates its own tables on first run; `npm run db:remote` is only needed if you add new migrations later.

## Notes on the port

This is a TypeScript rewrite of the original .NET/Blazor app, which is preserved under [`legacy/`](legacy/)
for reference. The rewrite covers the core money workflows above. Not yet ported: bank
register/reconciliation, PDF invoices and reports, donations and tax letters, trip imports, the
sign-in-app integration, authenticator-app (TOTP) 2FA, Google sign-in and the nightly backup export.

## License

MIT, see [LICENSE](LICENSE).
