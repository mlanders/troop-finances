import { Hono } from "hono";
import type { AppEnv } from "../types";
import { audit, requireUser } from "../lib/auth";
import { flashRedirect, formBody, page } from "../lib/page";
import { Money, StatusPill } from "../lib/view";
import { formatDate, newId, nowIso } from "../lib/util";
import { parseAmount } from "../lib/money";

export const portalRoutes = new Hono<AppEnv>();
portalRoutes.use("*", requireUser);

const MAX_RECEIPT_BYTES = 10 * 1024 * 1024;
const RECEIPT_TYPES: Record<string, string> = {
  "image/jpeg": "jpg", "image/png": "png", "image/webp": "webp", "image/heic": "heic", "application/pdf": "pdf",
};

portalRoutes.get("/", async (c) => {
  const u = c.get("user")!;
  if (!u.guardianId) return page(c, "My family", <p class="muted">Your email isn't linked to a guardian record. Ask the treasurer to add you.</p>);
  const scouts = (await c.env.DB.prepare(
    `SELECT s.id, s.full_name, COALESCE((SELECT SUM(amount_cents) FROM ledger l WHERE l.scout_id = s.id),0) AS balance
     FROM scouts s JOIN guardian_scouts gs ON gs.scout_id = s.id WHERE gs.guardian_id = ? ORDER BY s.full_name`,
  ).bind(u.guardianId).all<{ id: string; full_name: string; balance: number }>()).results;
  const invoices = (await c.env.DB.prepare(
    `SELECT i.id, i.description, i.due_cents, i.status, i.due_date, s.full_name FROM invoices i
     JOIN guardian_scouts gs ON gs.scout_id = i.scout_id JOIN scouts s ON s.id = i.scout_id
     WHERE gs.guardian_id = ? AND i.status = 'Outstanding' ORDER BY i.issued_at DESC`,
  ).bind(u.guardianId).all<{ id: string; description: string; due_cents: number; status: string; due_date: string | null; full_name: string }>()).results;
  return page(c, `Hello, ${u.name}`, (
    <>
      <div class="grid">
        {scouts.map((s) => (
          <div class="card stat"><span class="muted">{s.full_name}'s account</span><b><Money cents={s.balance} signed /></b><a href={`/portal/scouts/${s.id}`}>View activity</a></div>
        ))}
      </div>
      <h2>Open invoices</h2>
      {invoices.length === 0 ? <p class="muted">Nothing owed. 🎉</p> : (
        <div class="tablewrap"><table><thead><tr><th>Scout</th><th>For</th><th>Due</th><th class="right">Amount due</th></tr></thead><tbody>
          {invoices.map((i) => <tr><td>{i.full_name}</td><td>{i.description}</td><td>{formatDate(i.due_date)}</td><td class="right"><Money cents={i.due_cents} /></td></tr>)}
        </tbody></table></div>
      )}
      <p class="muted">Pay the treasurer by check, cash or Venmo; they'll record it here.</p>
      <p><a class="btn" href="/portal/receipts/new">Submit a receipt for reimbursement</a> <a class="btn secondary" href="/portal/receipts">My receipts</a></p>
    </>
  ));
});

portalRoutes.get("/scouts/:id", async (c) => {
  const u = c.get("user")!;
  const link = await c.env.DB.prepare("SELECT 1 FROM guardian_scouts WHERE guardian_id = ? AND scout_id = ?").bind(u.guardianId, c.req.param("id")).first();
  if (!link) return c.text("Not found", 404);
  const scout = await c.env.DB.prepare("SELECT full_name FROM scouts WHERE id = ?").bind(c.req.param("id")).first<{ full_name: string }>();
  const rows = (await c.env.DB.prepare("SELECT created_at, description, amount_cents FROM ledger WHERE scout_id = ? ORDER BY created_at DESC LIMIT 200")
    .bind(c.req.param("id")).all<{ created_at: string; description: string; amount_cents: number }>()).results;
  return page(c, `${scout?.full_name}'s account`, (
    <div class="tablewrap"><table><thead><tr><th>Date</th><th>Activity</th><th class="right">Amount</th></tr></thead><tbody>
      {rows.map((r) => <tr><td>{formatDate(r.created_at)}</td><td>{r.description}</td><td class="right"><Money cents={r.amount_cents} signed /></td></tr>)}
    </tbody></table></div>
  ));
});

portalRoutes.get("/receipts", async (c) => {
  const u = c.get("user")!;
  const rows = (await c.env.DB.prepare("SELECT id, submitted_at, description, amount_cents, status, review_notes FROM reimbursements WHERE guardian_id = ? ORDER BY submitted_at DESC")
    .bind(u.guardianId ?? "").all<{ id: string; submitted_at: string; description: string; amount_cents: number; status: string; review_notes: string | null }>()).results;
  return page(c, "My receipts", (
    <>
      <p><a class="btn" href="/portal/receipts/new">Submit a receipt</a></p>
      <div class="tablewrap"><table><thead><tr><th>Submitted</th><th>Description</th><th>Status</th><th class="right">Amount</th></tr></thead><tbody>
        {rows.map((r) => <tr><td>{formatDate(r.submitted_at)}</td><td>{r.description}{r.review_notes && <div class="muted">{r.review_notes}</div>}</td><td><StatusPill status={r.status} /></td><td class="right"><Money cents={r.amount_cents} /></td></tr>)}
      </tbody></table></div>
    </>
  ));
});

portalRoutes.get("/receipts/new", async (c) => {
  const u = c.get("user")!;
  if (!u.guardianId) return c.redirect("/portal");
  const scouts = (await c.env.DB.prepare("SELECT s.id, s.full_name FROM scouts s JOIN guardian_scouts gs ON gs.scout_id = s.id WHERE gs.guardian_id = ? ORDER BY s.full_name")
    .bind(u.guardianId).all<{ id: string; full_name: string }>()).results;
  return page(c, "Submit a receipt", (
    <form method="post" action="/portal/receipts/new" enctype="multipart/form-data" class="card narrow">
      <label for="amount">Amount you paid</label><input id="amount" name="amount" inputmode="decimal" placeholder="24.50" required />
      <label for="description">What was it for?</label><input id="description" name="description" required maxlength={300} />
      <label for="scout">Related scout (optional)</label>
      <select id="scout" name="scout_id"><option value="">None / troop expense</option>{scouts.map((s) => <option value={s.id}>{s.full_name}</option>)}</select>
      <label for="receipt">Photo or PDF of the receipt</label>
      <input id="receipt" name="receipt" type="file" accept="image/*,application/pdf" capture="environment" required />
      <p><button>Submit</button></p>
    </form>
  ));
});

portalRoutes.post("/receipts/new", async (c) => {
  const u = c.get("user")!;
  if (!u.guardianId) return c.redirect("/portal");
  const body = await c.req.parseBody();
  const cents = parseAmount(String(body.amount ?? ""));
  const description = String(body.description ?? "").trim();
  const file = body.receipt;
  if (!cents || cents <= 0 || !description || !(file instanceof File) || file.size === 0)
    return flashRedirect(c, "/portal/receipts/new", "Enter an amount, a description and a receipt file.", "err");
  const ext = RECEIPT_TYPES[file.type];
  if (!ext) return flashRedirect(c, "/portal/receipts/new", "Receipts must be a photo (JPEG, PNG, WebP, HEIC) or a PDF.", "err");
  if (file.size > MAX_RECEIPT_BYTES) return flashRedirect(c, "/portal/receipts/new", "That file is over 10 MB.", "err");
  let scoutId = String(body.scout_id ?? "") || null;
  if (scoutId) {
    const ok = await c.env.DB.prepare("SELECT 1 FROM guardian_scouts WHERE guardian_id = ? AND scout_id = ?").bind(u.guardianId, scoutId).first();
    if (!ok) scoutId = null;
  }
  const id = newId();
  const key = `receipts/${id}.${ext}`;
  await c.env.RECEIPTS.put(key, await file.arrayBuffer(), { httpMetadata: { contentType: file.type } });
  await c.env.DB.prepare(
    `INSERT INTO reimbursements (id, guardian_id, submitter_name, submitter_email, scout_id, amount_cents, description, receipt_key, receipt_type, receipt_name, submitted_at)
     VALUES (?,?,?,?,?,?,?,?,?,?,?)`,
  ).bind(id, u.guardianId, u.name, u.email, scoutId, cents, description, key, file.type, file.name.slice(0, 200), nowIso()).run();
  await audit(c.env, u.email, "reimbursement.submitted", id);
  return flashRedirect(c, "/portal/receipts", "Receipt submitted. The treasurer will review it.");
});
