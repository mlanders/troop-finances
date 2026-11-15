# Who can sign in, and what they can do

Two separate areas, each with its own sign-in: the **admin area** (`/admin`) for treasurers and
leaders, and the **parent portal** (`/portal`) for guardians.

## Admin area

Treasurers and leaders sign in at `/admin/signin` with a Microsoft account (work, school or
personal) or a Google account. Each provider's button appears only once its client id is set
(see [infra/DEPLOY.md](../infra/DEPLOY.md) step 2 or [infra/SETUP.md](../infra/SETUP.md)).

Signing in alone grants nothing. Access is stored in the app's `AdminUsers` table and managed by
Editors at **Settings > People & access** (`/admin/settings/access`).

### Roles

- **Read only** can view everything; **Edit** can also make changes, including who has access.
- Someone signing in for the first time with no role sees a "contact an administrator" page and
  appears on People & access as an **access request**, where an Editor gives them a role or
  dismisses the request.
- Editors can't change their own access, and the last Editor can't be demoted or removed.
- One person can be marked **committee chair**, who gets a copy of the "Edit access granted"
  email.

### The first Editor

The very first Editor comes from `Auth:BootstrapEditors`, and only while no Editor exists yet: a
listed address signing in becomes an Editor. The address must be one the provider vouches for: a
Google-verified address, or a Microsoft account's sign-in name.

### Global admins

Addresses in `Auth:GlobalAdmins` (provider-verified, like the bootstrap list) get Read only and
can always turn on temporary Edit. They can also allow others to, even while Read only. It's set
only from configuration; People & access shows a "Global admin" badge.

### Temporary Edit

A Global admin, or a Read-only person an Editor or Global admin has allowed, can turn on Edit for
themselves for 1, 2, 4 or 8 hours (`/admin/settings/edit-access`), giving a reason.

- Each time, an email goes to the standing Editors, the committee chair and the
  `Auth:EditorGrantNotify` addresses. This alert can't be turned off.
- It's recorded in Activity History, ends on its own, and can't change who has access.
- The person's stored role stays Read only, so it never counts as an Editor for the last-Editor
  or bootstrap checks.
- Allowing someone takes a **last day** (today up to a year out, in the troop's time zone).
  After that day they can't turn it on, and any Edit they have on ends at midnight. People &
  access shows the date and lets an Editor change it or stop it early. A Global admin's own
  permission has no end date.

### When changes take effect

The role is looked up on every request, not stored in the sign-in cookie, so a change applies
within a minute or two, open pages included (`AdminRoleClaimsTransformation`,
`AdminRevalidatingAuthStateProvider`).

### Saved list views

The **Invoices** and **Reimbursements** lists filter by status and, behind the **Filters**
button, by date range, scout or guardian, and description. Any admin can save the current filters
as a named **view** (e.g. "Summer camp 2026": description "summer camp", issued Jun 1 - Aug 31)
that every admin can then pick from the View list; `?view=<id>` links straight to one. Whoever
saved a view, or any Editor, can delete it. Views live in the `ListViews` table (backed up
nightly), and saving or deleting one is recorded in Activity History.

## Parent portal

Parents and guardians sign in with an **emailed one-time code**: no password, and no Microsoft
or Google account needed. Once the treasurer links them to a scout (under `/admin/guardians`),
they can submit reimbursement receipts and see that scout's balance and invoice history. The
sign-in lasts 30 days and renews as they use it, so the home-screen app doesn't ask for a new code
every time.

### Authenticator apps

As an alternative, a parent can link an **authenticator app** (Google or Microsoft
Authenticator, 1Password, Apple Passwords, ...) under **Sign-in options**
(`/portal/sign-in-options`, reached by tapping their name in the header). They then sign in with
their email plus the app's current code, using "Use my authenticator app" on the sign-in page.

- Emailed codes always keep working, so a lost phone never locks anyone out.
- From a guardian's row menu on the Guardians list, an Editor can **set up the app in person**
  (show the QR code for the parent to scan, then type in the code it shows) or remove a parent's
  app. Both are recorded in Activity History.
- The shared secret is stored AES-GCM-encrypted with a key derived from `Auth:LoginCodePepper`.
  Each code works once, and five wrong codes lock app sign-in for 15 minutes.
