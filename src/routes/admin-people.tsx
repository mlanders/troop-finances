import { Hono } from "hono";
import type { AppEnv } from "../types";
import { audit, bootstrapAdmins } from "../lib/auth";
import { flashRedirect, formBody, page } from "../lib/page";
import { Money, StatusPill } from "../lib/view";
import { formatDate, isEmail, newId, normalizeEmail, nowIso } from "../lib/util";
import { parseAmount } from "../lib/money";
import { ledgerStmt, scoutBalance } from "../lib/ledger";

export const peopleRoutes = new Hono<AppEnv>();

peopleRoutes.get("/", async (c) => {
  const db = c.env.DB;
  const one = async (sql: string) => (await db.prepare(sql).first<{ n: number; t: number }>()) ?? { n: 0, t: 0 };
  const pending = await one("SELECT COUNT(*) n, COALESCE(SUM(amount_cents),0) t FROM reimbursements WHERE status = 'Pending'");
  const toPay = await one("SELECT COUNT(*) n, COALESCE(SUM(amount_cents),0) t FROM reimbursements WHERE status = 'Approved'");
  const owed = await one("SELECT COUNT(*) n, COALESCE(SUM(due_cents),0) t FROM invoices WHERE status = 'Outstanding'");
  const accounts = await one("SELECT COUNT(*) n, COALESCE(SUM(amount_cents),0) t FROM ledger");
  const recent = (await db.prepare("SELECT at, actor, action, detail FROM audit ORDER BY at DESC LIMIT 15").all<{ at: string; actor: string; action: string; detail: string }>()).results;
  return page(c, "Dashboard", (
    <>
      <div class="grid">
        <a class="card stat" href="/admin/reimbursements"><span class="muted">Receipts to review</span><b>{pending.n}</b><Money cents={pending.t} /></a>
        <a class="card stat" href="/admin/reimbursements?status=Approved"><span class="muted">Approved, awaiting payment</span><b>{toPay.n}</b><Money cents={toPay.t} /></a>
        <a class="card stat" href="/admin/invoices"><span class="muted">Outstanding invoices</span><b><Money cents={owed.t} /></b>{owed.n} open</a>
        <div class="card stat"><span class="muted">Held in scout accounts</span><b><Money cents={accounts.t} /></b></div>
      </div>
      <h2>Recent activity</h2>
      <div class="tablewrap"><table><thead><tr><th>When</th><th>Who</th><th>What</th></tr></thead><tbody>
        {recent.map((r) => <tr><td>{formatDate(r.at)}</td><td>{r.actor}</td><td>{r.action} <span class="muted">{r.detail}</span></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

// ---- Scouts ----
peopleRoutes.get("/scouts", async (c) => {
  const rows = (await c.env.DB.prepare(
    `SELECT s.id, s.full_name, s.rank, s.patrol, s.is_active, COALESCE((SELECT SUM(amount_cents) FROM ledger l WHERE l.scout_id = s.id),0) AS balance
     FROM scouts s ORDER BY s.is_active DESC, s.full_name`).all<any>()).results;
  return page(c, "Scouts", (
    <>
      <p><a class="btn" href="/admin/scouts/new">Add scouts</a></p>
      <div class="tablewrap"><table><thead><tr><th>Name</th><th>Rank</th><th>Patrol</th><th class="right">Balance</th></tr></thead><tbody>
        {rows.map((s) => <tr><td><a href={`/admin/scouts/${s.id}`}>{s.full_name}</a>{!s.is_active && <span class="pill"> inactive</span>}</td><td>{s.rank}</td><td>{s.patrol}</td><td class="right"><Money cents={s.balance} signed /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

peopleRoutes.get("/scouts/new", (c) =>
  page(c, "Add scouts", (
    <form method="post" class="card narrow">
      <label for="names">One scout per line. Optional extras after commas: <code>Name, Rank, Patrol</code></label>
      <textarea id="names" name="names" rows={10} required placeholder={"Alex Rivera, Scout, Eagle Patrol\nSam Chen"} />
      <p><button>Add</button></p>
    </form>
  )),
);

peopleRoutes.post("/scouts/new", async (c) => {
  const f = await formBody(c);
  const u = c.get("user")!;
  const stmts = (f.names ?? "").split("\n").map((l) => l.split(",").map((p) => p.trim())).filter((p) => p[0])
    .map(([name, rank, patrol]) => c.env.DB.prepare("INSERT INTO scouts (id, full_name, rank, patrol, created_at) VALUES (?,?,?,?,?)")
      .bind(newId(), name, rank || null, patrol || null, nowIso()));
  if (!stmts.length) return flashRedirect(c, "/admin/scouts/new", "Enter at least one name.", "err");
  await c.env.DB.batch(stmts);
  await audit(c.env, u.email, "scouts.added", `${stmts.length}`);
  return flashRedirect(c, "/admin/scouts", `Added ${stmts.length} scout(s).`);
});

peopleRoutes.get("/scouts/:id", async (c) => {
  const id = c.req.param("id");
  const s = await c.env.DB.prepare("SELECT * FROM scouts WHERE id = ?").bind(id).first<any>();
  if (!s) return page(c, "Not found", <p>No such scout.</p>, 404);
  const balance = await scoutBalance(c.env, id);
  const ledger = (await c.env.DB.prepare("SELECT created_at, description, amount_cents, created_by FROM ledger WHERE scout_id = ? ORDER BY created_at DESC LIMIT 300").bind(id).all<any>()).results;
  const linked = (await c.env.DB.prepare("SELECT g.id, g.full_name, g.email FROM guardians g JOIN guardian_scouts gs ON gs.guardian_id = g.id WHERE gs.scout_id = ?").bind(id).all<any>()).results;
  const allG = (await c.env.DB.prepare("SELECT id, full_name FROM guardians WHERE is_active = 1 ORDER BY full_name").all<any>()).results;
  return page(c, s.full_name, (
    <>
      <div class="card stat"><span class="muted">Account balance</span><b><Money cents={balance} signed /></b></div>
      <h2>Guardians</h2>
      <ul>{linked.map((g) => <li>{g.full_name} <span class="muted">{g.email}</span>
        <form method="post" action={`/admin/scouts/${id}/unlink`} style="display:inline"><input type="hidden" name="guardian_id" value={g.id} /> <button class="secondary">Unlink</button></form></li>)}</ul>
      <form method="post" action={`/admin/scouts/${id}/link`} class="row">
        <select name="guardian_id" required><option value="">Link a guardian…</option>{allG.map((g) => <option value={g.id}>{g.full_name}</option>)}</select><button>Link</button>
      </form>
      <h2>Adjust account</h2>
      <form method="post" action={`/admin/scouts/${id}/adjust`} class="card row">
        <select name="direction"><option value="credit">Credit (add)</option><option value="debit">Debit (remove)</option></select>
        <input name="amount" placeholder="Amount" inputmode="decimal" required style="max-width:140px" />
        <input name="description" placeholder="Reason" required style="flex:1;min-width:200px" /><button>Record</button>
      </form>
      <h2>Edit</h2>
      <form method="post" action={`/admin/scouts/${id}/edit`} class="card row">
        <input name="full_name" value={s.full_name} required /><input name="rank" value={s.rank ?? ""} placeholder="Rank" /><input name="patrol" value={s.patrol ?? ""} placeholder="Patrol" />
        <select name="is_active"><option value="1" selected={!!s.is_active}>Active</option><option value="0" selected={!s.is_active}>Inactive</option></select><button>Save</button>
      </form>
      <h2>Ledger</h2>
      <div class="tablewrap"><table><thead><tr><th>Date</th><th>Description</th><th>By</th><th class="right">Amount</th></tr></thead><tbody>
        {ledger.map((l) => <tr><td>{formatDate(l.created_at)}</td><td>{l.description}</td><td class="muted">{l.created_by}</td><td class="right"><Money cents={l.amount_cents} signed /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

peopleRoutes.post("/scouts/:id/edit", async (c) => {
  const f = await formBody(c); const id = c.req.param("id");
  if (!f.full_name?.trim()) return flashRedirect(c, `/admin/scouts/${id}`, "Name is required.", "err");
  await c.env.DB.prepare("UPDATE scouts SET full_name=?, rank=?, patrol=?, is_active=? WHERE id=?")
    .bind(f.full_name.trim(), f.rank?.trim() || null, f.patrol?.trim() || null, f.is_active === "0" ? 0 : 1, id).run();
  await audit(c.env, c.get("user")!.email, "scout.edited", id);
  return flashRedirect(c, `/admin/scouts/${id}`, "Saved.");
});

peopleRoutes.post("/scouts/:id/link", async (c) => {
  const f = await formBody(c); const id = c.req.param("id");
  if (f.guardian_id) await c.env.DB.prepare("INSERT OR IGNORE INTO guardian_scouts (guardian_id, scout_id) VALUES (?,?)").bind(f.guardian_id, id).run();
  return flashRedirect(c, `/admin/scouts/${id}`, "Linked.");
});

peopleRoutes.post("/scouts/:id/unlink", async (c) => {
  const f = await formBody(c); const id = c.req.param("id");
  await c.env.DB.prepare("DELETE FROM guardian_scouts WHERE guardian_id = ? AND scout_id = ?").bind(f.guardian_id, id).run();
  return flashRedirect(c, `/admin/scouts/${id}`, "Unlinked.");
});

peopleRoutes.post("/scouts/:id/adjust", async (c) => {
  const f = await formBody(c); const id = c.req.param("id"); const u = c.get("user")!;
  const cents = parseAmount(f.amount);
  if (!cents || !f.description?.trim()) return flashRedirect(c, `/admin/scouts/${id}`, "Enter a valid amount and a reason.", "err");
  const signed = f.direction === "debit" ? -cents : cents;
  await ledgerStmt(c.env, { scoutId: id, cents: signed, type: "ManualAdjustment", description: f.description.trim(), by: u.email }).run();
  await audit(c.env, u.email, "scout.adjusted", `${id} ${signed}`);
  return flashRedirect(c, `/admin/scouts/${id}`, "Recorded.");
});

// ---- Guardians ----
peopleRoutes.get("/guardians", async (c) => {
  const rows = (await c.env.DB.prepare(
    `SELECT g.id, g.full_name, g.email, g.is_active, GROUP_CONCAT(s.full_name, ', ') AS scouts FROM guardians g
     LEFT JOIN guardian_scouts gs ON gs.guardian_id = g.id LEFT JOIN scouts s ON s.id = gs.scout_id GROUP BY g.id ORDER BY g.full_name`).all<any>()).results;
  return page(c, "Guardians", (
    <>
      <form method="post" action="/admin/guardians/new" class="card">
        <p class="muted">One per line: <code>Name, email</code></p>
        <textarea name="people" rows={4} placeholder="Pat Rivera, pat@example.com" required />
        <p><button>Add guardians</button></p>
      </form>
      <div class="tablewrap"><table><thead><tr><th>Name</th><th>Email</th><th>Scouts</th></tr></thead><tbody>
        {rows.map((g) => <tr><td>{g.full_name}{!g.is_active && <span class="pill"> inactive</span>}</td><td>{g.email}</td><td>{g.scouts}</td></tr>)}
      </tbody></table></div>
    </>
  ));
});

peopleRoutes.post("/guardians/new", async (c) => {
  const f = await formBody(c);
  const people = (f.people ?? "").split("\n").map((l) => l.split(",").map((p) => p.trim())).filter((p) => p[0]);
  const bad = people.filter(([, e]) => !e || !isEmail(e));
  if (!people.length || bad.length) return flashRedirect(c, "/admin/guardians", "Each line needs a name and a valid email.", "err");
  await c.env.DB.batch(people.map(([name, email]) =>
    c.env.DB.prepare("INSERT OR IGNORE INTO guardians (id, full_name, email, created_at) VALUES (?,?,?,?)").bind(newId(), name, normalizeEmail(email), nowIso())));
  await audit(c.env, c.get("user")!.email, "guardians.added", `${people.length}`);
  return flashRedirect(c, "/admin/guardians", `Processed ${people.length} guardian(s).`);
});

// ---- Settings / admins ----
peopleRoutes.get("/settings", async (c) => {
  const admins = (await c.env.DB.prepare("SELECT email, name FROM admins ORDER BY email").all<any>()).results;
  const boot = bootstrapAdmins(c.env);
  return page(c, "Settings", (
    <>
      <h2>Admins</h2>
      <p class="muted">Admins sign in with the same email-code flow. Addresses in the <code>ADMIN_EMAILS</code> setting are always admins.</p>
      <ul>
        {boot.map((e) => <li>{e} <span class="pill">from ADMIN_EMAILS</span></li>)}
        {admins.map((a) => <li>{a.email} <form method="post" action="/admin/settings/admins/remove" style="display:inline"><input type="hidden" name="email" value={a.email} /> <button class="secondary">Remove</button></form></li>)}
      </ul>
      <form method="post" action="/admin/settings/admins" class="card row"><input name="email" type="email" placeholder="treasurer@example.org" required /><button>Add admin</button></form>
    </>
  ));
});

peopleRoutes.post("/settings/admins", async (c) => {
  const f = await formBody(c); const email = normalizeEmail(f.email ?? "");
  if (!isEmail(email)) return flashRedirect(c, "/admin/settings", "Enter a valid email.", "err");
  await c.env.DB.prepare("INSERT OR IGNORE INTO admins (email, created_at) VALUES (?,?)").bind(email, nowIso()).run();
  await audit(c.env, c.get("user")!.email, "admin.added", email);
  return flashRedirect(c, "/admin/settings", "Admin added.");
});

peopleRoutes.post("/settings/admins/remove", async (c) => {
  const f = await formBody(c); const email = normalizeEmail(f.email ?? "");
  if (email === c.get("user")!.email) return flashRedirect(c, "/admin/settings", "You can't remove yourself.", "err");
  await c.env.DB.prepare("DELETE FROM admins WHERE email = ?").bind(email).run();
  await audit(c.env, c.get("user")!.email, "admin.removed", email);
  return flashRedirect(c, "/admin/settings", "Admin removed.");
});
