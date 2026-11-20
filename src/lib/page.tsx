import type { Context } from "hono";
import { getCookie, setCookie, deleteCookie } from "hono/cookie";
import type { AppEnv } from "../types";
import { Layout } from "./view";

/** Renders a page inside the layout, picking up a one-shot flash message from a cookie. */
export function page(c: Context<AppEnv>, title: string, body: unknown, status: 200 | 400 | 404 = 200) {
  const raw = getCookie(c, "tf_flash");
  let flash: { kind: "ok" | "err"; text: string } | null = null;
  if (raw) {
    try { flash = JSON.parse(decodeURIComponent(raw)); } catch { /* ignore */ }
    deleteCookie(c, "tf_flash", { path: "/" });
  }
  c.status(status);
  return c.html(
    <Layout title={title} troop={c.env.TROOP_NAME || "Troop"} user={c.get("user")} flash={flash}>
      {body as any}
    </Layout>,
  );
}

export function flashRedirect(c: Context<AppEnv>, to: string, text: string, kind: "ok" | "err" = "ok") {
  setCookie(c, "tf_flash", encodeURIComponent(JSON.stringify({ kind, text })), { path: "/", maxAge: 30, sameSite: "Lax" });
  return c.redirect(to);
}

export async function formBody(c: Context<AppEnv>): Promise<Record<string, string>> {
  const out: Record<string, string> = {};
  const fd = await c.req.parseBody({ all: false });
  for (const [k, v] of Object.entries(fd)) if (typeof v === "string") out[k] = v;
  return out;
}
