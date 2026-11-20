import type { FC, PropsWithChildren } from "hono/jsx";
import { raw } from "hono/html";
import type { User } from "../types";

const css = `
:root{--bg:#f6f7f9;--fg:#1c2330;--muted:#667085;--card:#fff;--line:#e3e6eb;--brand:#1f5f3a;--brand-fg:#fff;--bad:#b42318;--warn:#b54708;--ok:#067647}
@media (prefers-color-scheme:dark){:root{--bg:#10141a;--fg:#e8ebf0;--muted:#98a2b3;--card:#181d26;--line:#2a3140;--brand:#3d9a64;--bad:#f97066;--warn:#fdb022;--ok:#47cd89}}
*{box-sizing:border-box}body{margin:0;font:16px/1.45 system-ui,sans-serif;background:var(--bg);color:var(--fg)}
header{background:var(--brand);color:var(--brand-fg);padding:.6rem 1rem;display:flex;flex-wrap:wrap;gap:.5rem 1rem;align-items:center}
header a{color:inherit;text-decoration:none;font-weight:500}header .brand{font-weight:700;margin-right:auto}
main{max-width:980px;margin:0 auto;padding:1rem}
h1{font-size:1.5rem;margin:.5rem 0 1rem}h2{font-size:1.15rem;margin:1.5rem 0 .5rem}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:1rem;margin-bottom:1rem}
.grid{display:grid;gap:1rem;grid-template-columns:repeat(auto-fit,minmax(200px,1fr))}
.stat b{display:block;font-size:1.6rem}.muted{color:var(--muted)}.right{text-align:right}
table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{padding:.55rem .7rem;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}th{font-size:.8rem;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
tr:last-child td{border-bottom:0}.tablewrap{overflow-x:auto}
a{color:var(--brand)}label{display:block;font-weight:600;margin:.8rem 0 .25rem}
input,select,textarea{width:100%;padding:.55rem;border:1px solid var(--line);border-radius:8px;background:var(--bg);color:var(--fg);font:inherit}
button,.btn{display:inline-block;background:var(--brand);color:var(--brand-fg);border:0;border-radius:8px;padding:.55rem 1rem;font:inherit;font-weight:600;cursor:pointer;text-decoration:none}
button.secondary,.btn.secondary{background:transparent;color:var(--fg);border:1px solid var(--line)}button.danger{background:var(--bad)}
.pill{display:inline-block;padding:.05rem .55rem;border-radius:999px;font-size:.8rem;border:1px solid var(--line)}
.pill.ok{color:var(--ok)}.pill.warn{color:var(--warn)}.pill.bad{color:var(--bad)}
.flash{padding:.6rem .8rem;border-radius:8px;margin-bottom:1rem;border:1px solid var(--line)}.flash.err{color:var(--bad)}.flash.ok{color:var(--ok)}
.row{display:flex;gap:.5rem;flex-wrap:wrap;align-items:center}.row>*{width:auto}
.pos{color:var(--ok)}.neg{color:var(--bad)}.narrow{max-width:460px}
`;

export const Layout: FC<PropsWithChildren<{ title: string; troop: string; user: User | null; flash?: { kind: "ok" | "err"; text: string } | null }>> = (p) => (
  <>
    {raw("<!doctype html>")}
    <html lang="en">
      <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width,initial-scale=1" />
        <title>{p.title} · {p.troop} Finances</title>
        <style dangerouslySetInnerHTML={{ __html: css }} />
      </head>
      <body>
        <header>
          <a class="brand" href="/">{p.troop} Finances</a>
          {p.user?.isAdmin && (
            <>
              <a href="/admin">Dashboard</a>
              <a href="/admin/scouts">Scouts</a>
              <a href="/admin/guardians">Guardians</a>
              <a href="/admin/invoices">Invoices</a>
              <a href="/admin/fundraisers">Fundraisers</a>
              <a href="/admin/reimbursements">Reimbursements</a>
              <a href="/admin/settings">Settings</a>
            </>
          )}
          {p.user?.guardianId && <a href="/portal">My family</a>}
          {p.user && (
            <form method="post" action="/logout" style="margin:0">
              <button class="secondary" style="color:inherit;border-color:rgba(255,255,255,.5)">Sign out</button>
            </form>
          )}
        </header>
        <main>
          {p.flash && <div class={`flash ${p.flash.kind}`}>{p.flash.text}</div>}
          <h1>{p.title}</h1>
          {p.children}
        </main>
      </body>
    </html>
  </>
);

export const StatusPill: FC<{ status: string }> = ({ status }) => {
  const cls = ["Paid", "Approved", "Open"].includes(status) ? "ok" : ["Rejected", "Voided"].includes(status) ? "bad" : "warn";
  return <span class={`pill ${cls}`}>{status}</span>;
};

export const Money: FC<{ cents: number; signed?: boolean }> = ({ cents, signed }) => (
  <span class={signed ? (cents < 0 ? "neg" : "pos") : ""}>{new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(cents / 100)}</span>
);
