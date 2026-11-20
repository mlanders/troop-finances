import { Hono } from "hono";
import type { AppEnv } from "../types";
import { audit } from "../lib/auth";
import { flashRedirect, formBody, page } from "../lib/page";
import { Money, StatusPill } from "../lib/view";
import { formatDate, newId, nowIso } from "../lib/util";
import { parseAmount, planInvoice, splitEvenly } from "../lib/money";
import { ledgerStmt, scoutBalance } from "../lib/ledger";

export const moneyRoutes = new Hono<AppEnv>();

const METHODS = ["Check", "Cash", "Venmo", "Other"];

// ---- Invoices ----
moneyRoutes.get("/invoices", async (c) => {
  const status = c.req.query("status") ?? "Outstanding";
  const rows = (await c.env.DB.prepare(
    `SELECT i.id, i.description, i.total_cents, i.due_cents, i.status, i.issued_at, s.full_name FROM invoices i JOIN scouts s ON s.id = i.scout_id
     WHERE (? = 'All' OR i.status = ?) ORDER BY i.issued_at DESC LIMIT 300`).bind(status, status).all<any>()).results;
  return page(c, "Invoices", (
    <>
      <p class="row"><a class="btn" href="/admin/invoices/new">New invoice</a>
        {["Outstanding", "Paid", "Voided", "All"].map((s) => <a class={`btn ${s === status ? "" : "secondary"}`} href={`/admin/invoices?status=${s}`}>{s}</a>)}</p>
      <div class="tablewrap"><table><thead><tr><th>Issued</th><th>Scout</th><th>For</th><th>Status</th><th class="right">Total</th><th class="right">Due from family</th></tr></thead><tbody>
        {rows.map((i) => <tr><td>{formatDate(i.issued_at)}</td><td>{i.full_name}</td><td><a href={`/admin/invoices/${i.id}`}>{i.description}</a></td><td><StatusPill status={i.status} /></td><td class="right"><Money cents={i.total_cents} /></td><td class="right"><Money cents={i.due_cents} /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

moneyRoutes.get("/invoices/new", async (c) => {
  const scouts = (await c.env.DB.prepare("SELECT id, full_name FROM scouts WHERE is_active = 1 ORDER BY full_name").all<any>()).results;
  return page(c, "New invoice", (
    <form method="post" class="card narrow">
      <label for="description">Description</label><input id="description" name="description" required placeholder="Summer camp deposit" />
      <label for="amount">Amount per scout</label><input id="amount" name="amount" inputmode="decimal" required />
      <label for="due">Due date (optional)</label><input id="due" name="due_date" type="date" />
      <label for="scouts">Scouts (hold Ctrl/Cmd to pick several)</label>
      <select id="scouts" name="scout_id" multiple size={Math.min(12, Math.max(4, scouts.length))} required>{scouts.map((s) => <option value={s.id}>{s.full_name}</option>)}</select>
      <p class="muted">Money already in a scout's account is applied first; the family owes the rest.</p>
      <p><button>Issue</button></p>
    </form>
  ));
});

moneyRoutes.post("/invoices/new", async (c) => {
  const body = await c.req.parseBody({ all: true });
  const u = c.get("user")!;
  const cents = parseAmount(String(body.amount ?? ""));
  const description = String(body.description ?? "").trim();
  const ids = ([] as unknown[]).concat(body.scout_id ?? []).map(String);
  if (!cents || cents <= 0 || !description || !ids.length) return flashRedirect(c, "/admin/invoices/new", "Description, amount and at least one scout are required.", "err");
  const due = String(body.due_date ?? "") || null;
  for (const scoutId of ids) {
    const balance = await scoutBalance(c.env, scoutId);
    const plan = planInvoice(cents, balance);
    const invId = newId();
    const stmts = [
      c.env.DB.prepare(`INSERT INTO invoices (id, scout_id, description, total_cents, applied_cents, due_cents, status, issued_at, issued_by, due_date, paid_at)
        VALUES (?,?,?,?,?,?,?,?,?,?,?)`).bind(invId, scoutId, description, cents, plan.appliedCents, plan.dueCents,
        plan.dueCents === 0 ? "Paid" : "Outstanding", nowIso(), u.email, due, plan.dueCents === 0 ? nowIso() : null),
    ];
    if (plan.appliedCents > 0) stmts.push(ledgerStmt(c.env, { scoutId, cents: -plan.appliedCents, type: "InvoiceApplied", sourceId: invId, description: `Applied to invoice: ${description}`, by: u.email }));
    await c.env.DB.batch(stmts);
  }
  await audit(c.env, u.email, "invoices.issued", `${ids.length} × ${cents} "${description}"`);
  return flashRedirect(c, "/admin/invoices", `Issued ${ids.length} invoice(s).`);
});

moneyRoutes.get("/invoices/:id", async (c) => {
  const id = c.req.param("id");
  const i = await c.env.DB.prepare("SELECT i.*, s.full_name FROM invoices i JOIN scouts s ON s.id = i.scout_id WHERE i.id = ?").bind(id).first<any>();
  if (!i) return page(c, "Not found", <p>No such invoice.</p>, 404);
  const pays = (await c.env.DB.prepare("SELECT * FROM invoice_payments WHERE invoice_id = ? ORDER BY paid_at").bind(id).all<any>()).results;
  const paid = pays.reduce((a, p) => a + p.amount_cents, 0);
  const remaining = Math.max(0, i.due_cents - paid);
  return page(c, i.description, (
    <>
      <div class="card">
        <p><b>{i.full_name}</b> · <StatusPill status={i.status} /> · issued {formatDate(i.issued_at)}{i.due_date && ` · due ${formatDate(i.due_date)}`}</p>
        <p>Total <Money cents={i.total_cents} /> · from scout's account <Money cents={i.applied_cents} /> · owed by family <Money cents={i.due_cents} /> · paid <Money cents={paid} /></p>
        {i.status === "Voided" && <p class="neg">Voided: {i.void_reason}</p>}
      </div>
      {i.status === "Outstanding" && (
        <>
          <h2>Record a payment</h2>
          <form method="post" action={`/admin/invoices/${id}/pay`} class="card row">
            <input name="amount" value={(remaining / 100).toFixed(2)} inputmode="decimal" style="max-width:120px" required />
            <select name="method">{METHODS.map((m) => <option>{m}</option>)}</select>
            <input name="reference" placeholder="Check # / note" style="flex:1;min-width:160px" /><button>Record</button>
          </form>
          {paid === 0 && (
            <form method="post" action={`/admin/invoices/${id}/void`} class="card row">
              <input name="reason" placeholder="Reason for voiding" required style="flex:1;min-width:200px" /><button class="danger">Void invoice</button>
            </form>
          )}
        </>
      )}
      <h2>Payments</h2>
      <div class="tablewrap"><table><thead><tr><th>Date</th><th>Method</th><th>Reference</th><th class="right">Amount</th></tr></thead><tbody>
        {pays.map((p) => <tr><td>{formatDate(p.paid_at)}</td><td>{p.method}</td><td>{p.reference}</td><td class="right"><Money cents={p.amount_cents} /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

moneyRoutes.post("/invoices/:id/pay", async (c) => {
  const id = c.req.param("id"); const f = await formBody(c); const u = c.get("user")!;
  const i = await c.env.DB.prepare("SELECT due_cents, status FROM invoices WHERE id = ?").bind(id).first<any>();
  const cents = parseAmount(f.amount);
  if (!i || i.status !== "Outstanding") return flashRedirect(c, `/admin/invoices/${id}`, "That invoice isn't open.", "err");
  if (!cents || cents <= 0 || !METHODS.includes(f.method)) return flashRedirect(c, `/admin/invoices/${id}`, "Enter a valid amount and method.", "err");
  if (f.method === "Other" && !f.reference?.trim()) return flashRedirect(c, `/admin/invoices/${id}`, "Say how it was paid.", "err");
  const paidSoFar = (await c.env.DB.prepare("SELECT COALESCE(SUM(amount_cents),0) p FROM invoice_payments WHERE invoice_id = ?").bind(id).first<{ p: number }>())!.p;
  if (paidSoFar + cents > i.due_cents) return flashRedirect(c, `/admin/invoices/${id}`, "That is more than the remaining balance.", "err");
  const stmts = [c.env.DB.prepare("INSERT INTO invoice_payments (id, invoice_id, amount_cents, method, reference, recorded_by, paid_at) VALUES (?,?,?,?,?,?,?)")
    .bind(newId(), id, cents, f.method, (f.reference ?? "").trim(), u.email, nowIso())];
  if (paidSoFar + cents === i.due_cents) stmts.push(c.env.DB.prepare("UPDATE invoices SET status='Paid', paid_at=? WHERE id=?").bind(nowIso(), id));
  await c.env.DB.batch(stmts);
  await audit(c.env, u.email, "invoice.payment", `${id} ${cents} ${f.method}`);
  return flashRedirect(c, `/admin/invoices/${id}`, "Payment recorded.");
});

moneyRoutes.post("/invoices/:id/void", async (c) => {
  const id = c.req.param("id"); const f = await formBody(c); const u = c.get("user")!;
  const i = await c.env.DB.prepare("SELECT scout_id, applied_cents, description, status FROM invoices WHERE id = ?").bind(id).first<any>();
  const paid = (await c.env.DB.prepare("SELECT COUNT(*) n FROM invoice_payments WHERE invoice_id = ?").bind(id).first<{ n: number }>())!.n;
  if (!i || i.status !== "Outstanding" || paid > 0 || !f.reason?.trim()) return flashRedirect(c, `/admin/invoices/${id}`, "Can't void: needs a reason and no payments.", "err");
  const stmts = [c.env.DB.prepare("UPDATE invoices SET status='Voided', voided_at=?, voided_by=?, void_reason=? WHERE id=?").bind(nowIso(), u.email, f.reason.trim(), id)];
  if (i.applied_cents > 0) stmts.push(ledgerStmt(c.env, { scoutId: i.scout_id, cents: i.applied_cents, type: "InvoiceVoidReversal", sourceId: id, description: `Refund from voided invoice: ${i.description}`, by: u.email }));
  await c.env.DB.batch(stmts);
  await audit(c.env, u.email, "invoice.voided", id);
  return flashRedirect(c, `/admin/invoices/${id}`, "Voided.");
});

// ---- Fundraisers ----
moneyRoutes.get("/fundraisers", async (c) => {
  const rows = (await c.env.DB.prepare("SELECT * FROM fundraisers ORDER BY event_date DESC").all<any>()).results;
  return page(c, "Fundraisers", (
    <>
      <form method="post" action="/admin/fundraisers/new" class="card row">
        <input name="name" placeholder="Popcorn sale" required style="flex:1;min-width:180px" /><input name="event_date" type="date" required />
        <input name="raised" placeholder="Total raised" inputmode="decimal" style="max-width:130px" /><input name="expenses" placeholder="Expenses" inputmode="decimal" style="max-width:130px" /><button>Add</button>
      </form>
      <div class="tablewrap"><table><thead><tr><th>Date</th><th>Name</th><th>Status</th><th class="right">Net proceeds</th></tr></thead><tbody>
        {rows.map((r) => <tr><td>{formatDate(r.event_date)}</td><td><a href={`/admin/fundraisers/${r.id}`}>{r.name}</a></td><td><StatusPill status={r.status} /></td><td class="right"><Money cents={r.total_raised_cents - r.total_expenses_cents} /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

moneyRoutes.post("/fundraisers/new", async (c) => {
  const f = await formBody(c); const u = c.get("user")!;
  if (!f.name?.trim() || !f.event_date) return flashRedirect(c, "/admin/fundraisers", "Name and date are required.", "err");
  const id = newId();
  await c.env.DB.prepare("INSERT INTO fundraisers (id, name, event_date, total_raised_cents, total_expenses_cents, created_at, created_by) VALUES (?,?,?,?,?,?,?)")
    .bind(id, f.name.trim(), f.event_date, parseAmount(f.raised) ?? 0, parseAmount(f.expenses) ?? 0, nowIso(), u.email).run();
  await audit(c.env, u.email, "fundraiser.created", id);
  return flashRedirect(c, `/admin/fundraisers/${id}`, "Fundraiser created.");
});

moneyRoutes.get("/fundraisers/:id", async (c) => {
  const id = c.req.param("id");
  const fr = await c.env.DB.prepare("SELECT * FROM fundraisers WHERE id = ?").bind(id).first<any>();
  if (!fr) return page(c, "Not found", <p>No such fundraiser.</p>, 404);
  const net = fr.total_raised_cents - fr.total_expenses_cents;
  const allocs = (await c.env.DB.prepare("SELECT s.full_name, a.amount_cents FROM ledger a JOIN scouts s ON s.id = a.scout_id WHERE a.source_type='FundraiserAllocation' AND a.source_id = ? ORDER BY s.full_name").bind(id).all<any>()).results;
  const scouts = (await c.env.DB.prepare("SELECT id, full_name FROM scouts WHERE is_active = 1 ORDER BY full_name").all<any>()).results;
  return page(c, fr.name, (
    <>
      <div class="card"><p>{formatDate(fr.event_date)} · <StatusPill status={fr.status} /></p>
        <p>Raised <Money cents={fr.total_raised_cents} /> − expenses <Money cents={fr.total_expenses_cents} /> = net <b><Money cents={net} /></b></p></div>
      {fr.status === "Open" ? (
        <form method="post" action={`/admin/fundraisers/${id}/allocate`} class="card">
          <h2 style="margin-top:0">Split net proceeds evenly</h2>
          <p class="muted">Credits each selected scout's account and closes the fundraiser.</p>
          <select name="scout_id" multiple size={Math.min(12, Math.max(4, scouts.length))} required>{scouts.map((s) => <option value={s.id}>{s.full_name}</option>)}</select>
          <p><button disabled={net <= 0}>Allocate {net > 0 ? "" : "(no net proceeds)"}</button></p>
        </form>
      ) : (
        <div class="tablewrap"><table><thead><tr><th>Scout</th><th class="right">Share</th></tr></thead><tbody>
          {allocs.map((a) => <tr><td>{a.full_name}</td><td class="right"><Money cents={a.amount_cents} /></td></tr>)}
        </tbody></table></div>
      )}
    </>
  ));
});

moneyRoutes.post("/fundraisers/:id/allocate", async (c) => {
  const id = c.req.param("id"); const u = c.get("user")!;
  const body = await c.req.parseBody({ all: true });
  const ids = ([] as unknown[]).concat(body.scout_id ?? []).map(String);
  const fr = await c.env.DB.prepare("SELECT * FROM fundraisers WHERE id = ?").bind(id).first<any>();
  const net = fr ? fr.total_raised_cents - fr.total_expenses_cents : 0;
  if (!fr || fr.status !== "Open" || net <= 0 || !ids.length) return flashRedirect(c, `/admin/fundraisers/${id}`, "Nothing to allocate.", "err");
  const shares = splitEvenly(net, ids.length);
  const stmts = ids.map((scoutId, n) => ledgerStmt(c.env, { scoutId, cents: shares[n], type: "FundraiserAllocation", sourceId: id, description: `Fundraiser share: ${fr.name}`, by: u.email }));
  // Closing the fundraiser in the same batch (guarded on status) makes a double-submit a no-op.
  stmts.push(c.env.DB.prepare("UPDATE fundraisers SET status='Closed' WHERE id = ? AND status = 'Open'").bind(id));
  await c.env.DB.batch(stmts);
  await audit(c.env, u.email, "fundraiser.allocated", `${id} ${net} across ${ids.length}`);
  return flashRedirect(c, `/admin/fundraisers/${id}`, "Allocated.");
});

// ---- Reimbursements ----
moneyRoutes.get("/reimbursements", async (c) => {
  const status = c.req.query("status") ?? "Pending";
  const rows = (await c.env.DB.prepare("SELECT id, submitted_at, submitter_name, description, amount_cents, status FROM reimbursements WHERE (? = 'All' OR status = ?) ORDER BY submitted_at DESC LIMIT 300")
    .bind(status, status).all<any>()).results;
  return page(c, "Reimbursements", (
    <>
      <p class="row">{["Pending", "Approved", "Paid", "Rejected", "All"].map((s) => <a class={`btn ${s === status ? "" : "secondary"}`} href={`/admin/reimbursements?status=${s}`}>{s}</a>)}</p>
      <div class="tablewrap"><table><thead><tr><th>Submitted</th><th>By</th><th>For</th><th>Status</th><th class="right">Amount</th></tr></thead><tbody>
        {rows.map((r) => <tr><td>{formatDate(r.submitted_at)}</td><td>{r.submitter_name}</td><td><a href={`/admin/reimbursements/${r.id}`}>{r.description}</a></td><td><StatusPill status={r.status} /></td><td class="right"><Money cents={r.amount_cents} /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

moneyRoutes.get("/reimbursements/:id", async (c) => {
  const id = c.req.param("id");
  const r = await c.env.DB.prepare("SELECT r.*, s.full_name AS scout_name FROM reimbursements r LEFT JOIN scouts s ON s.id = r.scout_id WHERE r.id = ?").bind(id).first<any>();
  if (!r) return page(c, "Not found", <p>No such request.</p>, 404);
  return page(c, r.description, (
    <>
      <div class="card">
        <p><b>{r.submitter_name}</b> <span class="muted">{r.submitter_email}</span> · {formatDate(r.submitted_at)} · <StatusPill status={r.status} /></p>
        <p>Amount <b><Money cents={r.amount_cents} /></b>{r.scout_name && <> · scout {r.scout_name}</>}</p>
        <p><a class="btn secondary" href={`/admin/receipts/${id}`} target="_blank">View receipt ({r.receipt_name})</a></p>
        {r.review_notes && <p class="muted">Notes: {r.review_notes}</p>}
        {r.payment_note && <p class="muted">Payment: {r.payment_note}</p>}
      </div>
      {r.status === "Pending" && (
        <form method="post" action={`/admin/reimbursements/${id}/review`} class="card row">
          <input name="notes" placeholder="Notes (required to reject)" style="flex:1;min-width:200px" />
          <button name="decision" value="approve">Approve</button><button name="decision" value="reject" class="danger">Reject</button>
        </form>
      )}
      {r.status === "Approved" && (
        <form method="post" action={`/admin/reimbursements/${id}/pay`} class="card row">
          <input name="note" placeholder="How you paid (check #, Venmo…)" required style="flex:1;min-width:200px" /><button>Mark paid</button>
        </form>
      )}
    </>
  ));
});

moneyRoutes.post("/reimbursements/:id/review", async (c) => {
  const id = c.req.param("id"); const f = await formBody(c); const u = c.get("user")!;
  const approve = f.decision === "approve";
  if (!approve && !f.notes?.trim()) return flashRedirect(c, `/admin/reimbursements/${id}`, "Add a note explaining the rejection.", "err");
  const res = await c.env.DB.prepare("UPDATE reimbursements SET status=?, reviewed_at=?, reviewed_by=?, review_notes=? WHERE id=? AND status='Pending'")
    .bind(approve ? "Approved" : "Rejected", nowIso(), u.email, f.notes?.trim() || null, id).run();
  if (!res.meta.changes) return flashRedirect(c, `/admin/reimbursements/${id}`, "Already reviewed.", "err");
  await audit(c.env, u.email, approve ? "reimbursement.approved" : "reimbursement.rejected", id);
  return flashRedirect(c, "/admin/reimbursements", approve ? "Approved." : "Rejected.");
});

moneyRoutes.post("/reimbursements/:id/pay", async (c) => {
  const id = c.req.param("id"); const f = await formBody(c); const u = c.get("user")!;
  if (!f.note?.trim()) return flashRedirect(c, `/admin/reimbursements/${id}`, "Say how it was paid.", "err");
  const res = await c.env.DB.prepare("UPDATE reimbursements SET status='Paid', paid_at=?, paid_by=?, payment_note=? WHERE id=? AND status='Approved'")
    .bind(nowIso(), u.email, f.note.trim(), id).run();
  if (!res.meta.changes) return flashRedirect(c, `/admin/reimbursements/${id}`, "Not approved yet, or already paid.", "err");
  await audit(c.env, u.email, "reimbursement.paid", id);
  return flashRedirect(c, "/admin/reimbursements?status=Approved", "Marked paid.");
});

moneyRoutes.get("/receipts/:id", async (c) => {
  const r = await c.env.DB.prepare("SELECT receipt_key, receipt_type, receipt_name FROM reimbursements WHERE id = ?").bind(c.req.param("id")).first<any>();
  if (!r) return c.text("Not found", 404);
  const obj = await c.env.RECEIPTS.get(r.receipt_key);
  if (!obj) return c.text("Receipt file missing", 404);
  return new Response(obj.body, { headers: { "Content-Type": r.receipt_type, "Content-Disposition": `inline; filename="${encodeURIComponent(r.receipt_name)}"`, "X-Content-Type-Options": "nosniff", "Cache-Control": "private, no-store" } });
});
