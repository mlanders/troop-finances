import type { FC, PropsWithChildren } from "hono/jsx";
import { raw } from "hono/html";
import type { User } from "../types";

const css = `
:root{
  --navy:#0b2a5b;--navy-2:#143a7a;--red:#c8102e;--khaki:#efe6d2;--gold:#f2b705;
  --bg:#f4f1ea;--fg:#14213d;--muted:#5d6b82;--card:#ffffff;--line:#e2ddd0;--brand:var(--navy);--brand-fg:#fff;
  --bad:#b42318;--warn:#b45309;--ok:#0b7a43;--shadow:0 1px 2px rgba(11,42,91,.06),0 4px 14px rgba(11,42,91,.06);--radius:14px
}
@media (prefers-color-scheme:dark){:root{--bg:#0c1424;--fg:#e8edf6;--muted:#9aa9c2;--card:#142038;--line:#253456;--brand:#4f86e0;--brand-fg:#07142e;--khaki:#1b2a47;--bad:#ff8a80;--warn:#fbbf24;--ok:#4ade80;--shadow:0 1px 2px rgba(0,0,0,.4)}}
*{box-sizing:border-box}
body{margin:0;font:16px/1.5 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;background:var(--bg);color:var(--fg);-webkit-font-smoothing:antialiased}
header{background:linear-gradient(180deg,var(--navy-2),var(--navy));color:#fff;border-bottom:4px solid var(--red);position:sticky;top:0;z-index:10}
.bar{max-width:1040px;margin:0 auto;padding:.7rem 1rem;display:flex;flex-wrap:wrap;gap:.35rem .25rem;align-items:center}
.brand{font-weight:800;letter-spacing:.01em;font-size:1.1rem;margin-right:auto;color:#fff;text-decoration:none;display:flex;align-items:center;gap:.55rem}
.brand .mark{width:12px;height:26px;background:var(--gold);border-radius:3px;box-shadow:6px 0 0 var(--red)}
.brand span.sub{font-weight:500;opacity:.8}
nav{display:flex;flex-wrap:wrap;gap:.15rem;align-items:center}
nav a{color:#dbe5f7;text-decoration:none;font-weight:600;font-size:.92rem;padding:.4rem .65rem;border-radius:999px}
nav a:hover{background:rgba(255,255,255,.14);color:#fff}
header form button,header form button.secondary{background:rgba(255,255,255,.12);color:#fff;border:1px solid rgba(255,255,255,.35);padding:.4rem .8rem;font-size:.9rem}
header form button:hover{background:rgba(255,255,255,.22)}
main{max-width:1040px;margin:0 auto;padding:1.25rem 1rem 3rem}
h1{font-size:clamp(1.6rem,4vw,2.1rem);line-height:1.15;margin:.4rem 0 1.25rem;font-weight:800;letter-spacing:-.015em;color:var(--fg)}
h1::after{content:"";display:block;width:56px;height:4px;border-radius:2px;background:var(--red);margin-top:.6rem}
h2{font-size:.85rem;margin:1.8rem 0 .6rem;text-transform:uppercase;letter-spacing:.09em;color:var(--muted);font-weight:800}
.card{background:var(--card);border:1px solid var(--line);border-radius:var(--radius);padding:1.1rem;margin-bottom:1rem;box-shadow:var(--shadow)}
.grid{display:grid;gap:1rem;grid-template-columns:repeat(auto-fit,minmax(210px,1fr))}
.stat{text-decoration:none;color:inherit;border-top:4px solid var(--navy)}
.grid .stat:nth-child(2){border-top-color:var(--gold)}.grid .stat:nth-child(3){border-top-color:var(--red)}.grid .stat:nth-child(4){border-top-color:var(--ok)}
.stat b{display:block;font-size:1.9rem;line-height:1.2;font-weight:800;letter-spacing:-.02em;margin:.15rem 0}
a.stat:hover{transform:translateY(-1px);box-shadow:0 2px 4px rgba(11,42,91,.1),0 10px 24px rgba(11,42,91,.1)}
.muted{color:var(--muted)}.right{text-align:right}
.tablewrap{overflow-x:auto;border:1px solid var(--line);border-radius:var(--radius);background:var(--card);box-shadow:var(--shadow);margin-bottom:1rem}
table{width:100%;border-collapse:collapse}
th,td{padding:.7rem .9rem;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
th{font-size:.72rem;color:var(--muted);text-transform:uppercase;letter-spacing:.08em;background:var(--khaki);font-weight:800}
tbody tr:hover td{background:color-mix(in srgb,var(--khaki) 45%,transparent)}tr:last-child td{border-bottom:0}
a{color:var(--navy-2);font-weight:600}@media (prefers-color-scheme:dark){a{color:#8fb4f2}}
label{display:block;font-weight:700;margin:.9rem 0 .3rem;font-size:.92rem}
input,select,textarea{width:100%;padding:.65rem .75rem;border:1.5px solid var(--line);border-radius:10px;background:var(--card);color:var(--fg);font:inherit}
input:focus,select:focus,textarea:focus{outline:3px solid color-mix(in srgb,var(--navy-2) 30%,transparent);border-color:var(--navy-2)}
button,.btn{display:inline-block;background:var(--navy);color:#fff;border:0;border-radius:999px;padding:.6rem 1.2rem;font:inherit;font-weight:700;cursor:pointer;text-decoration:none;transition:filter .12s,transform .12s}
button:hover,.btn:hover{filter:brightness(1.15)}button:active,.btn:active{transform:translateY(1px)}
@media (prefers-color-scheme:dark){button,.btn{background:var(--brand);color:var(--brand-fg)}}
button.secondary,.btn.secondary{background:transparent;color:var(--fg);border:1.5px solid var(--line)}
button.danger{background:var(--red);color:#fff}button:disabled{opacity:.5;cursor:not-allowed}
.pill{display:inline-block;padding:.1rem .65rem;border-radius:999px;font-size:.75rem;font-weight:800;letter-spacing:.03em;border:1.5px solid currentColor}
.pill.ok{color:var(--ok)}.pill.warn{color:var(--warn)}.pill.bad{color:var(--bad)}
.flash{padding:.8rem 1rem;border-radius:12px;margin-bottom:1rem;background:var(--card);border:1px solid var(--line);border-left:5px solid var(--navy-2);box-shadow:var(--shadow)}
.flash.err{border-left-color:var(--red);color:var(--bad)}.flash.ok{border-left-color:var(--ok)}
.row{display:flex;gap:.55rem;flex-wrap:wrap;align-items:center}.row>*{width:auto}
.pos{color:var(--ok);font-weight:700}.neg{color:var(--bad);font-weight:700}.narrow{max-width:480px}
.login{max-width:480px;margin:2rem auto}
@media (max-width:640px){.bar{padding:.55rem .75rem}.bar form{order:2}nav{order:3;width:100%;overflow-x:auto;flex-wrap:nowrap;padding-bottom:.2rem}nav a{white-space:nowrap}th,td{padding:.6rem}}
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
          <div class="bar">
            <a class="brand" href="/"><i class="mark" aria-hidden="true"></i>{p.troop}<span class="sub">Finances</span></a>
            <nav aria-label="Main">
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
            </nav>
            {p.user && (
              <form method="post" action="/logout" style="margin:0">
                <button class="secondary">Sign out</button>
              </form>
            )}
          </div>
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
