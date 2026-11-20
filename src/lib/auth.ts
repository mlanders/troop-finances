import type { Context, MiddlewareHandler } from "hono";
import { deleteCookie, getCookie, setCookie } from "hono/cookie";
import type { AppEnv, Env, User } from "../types";
import { isEmail, newId, normalizeEmail, nowIso, randomCode, sha256Hex } from "./util";
import { sendEmail } from "./email";

const SESSION_COOKIE = "tf_session";
const SESSION_DAYS = 14;
const CODE_TTL_MS = 10 * 60 * 1000;
const RESEND_GAP_MS = 30 * 1000;
const MAX_ATTEMPTS = 5;

export function bootstrapAdmins(env: Env): string[] {
  return (env.ADMIN_EMAILS ?? "").split(",").map(normalizeEmail).filter(Boolean);
}

async function resolveUser(env: Env, email: string): Promise<User | null> {
  const guardian = await env.DB.prepare("SELECT id, full_name FROM guardians WHERE email = ? AND is_active = 1")
    .bind(email).first<{ id: string; full_name: string }>();
  const adminRow = await env.DB.prepare("SELECT name FROM admins WHERE email = ?").bind(email).first<{ name: string }>();
  const isAdmin = !!adminRow || bootstrapAdmins(env).includes(email);
  if (!guardian && !isAdmin) return null;
  return { email, isAdmin, guardianId: guardian?.id ?? null, name: guardian?.full_name || adminRow?.name || email };
}

/** Emails a one-time code. Always behaves the same whether or not the address is known (no account probing). */
export async function requestCode(env: Env, rawEmail: string): Promise<void> {
  const email = normalizeEmail(rawEmail);
  if (!isEmail(email)) return;
  const user = await resolveUser(env, email);
  if (!user) return;
  const prior = await env.DB.prepare("SELECT sent_at FROM login_codes WHERE email = ?").bind(email).first<{ sent_at: number }>();
  if (prior && Date.now() - prior.sent_at < RESEND_GAP_MS) return;
  const code = randomCode();
  const hash = await sha256Hex(`${env.CODE_PEPPER ?? ""}:${email}:${code}`);
  await env.DB.prepare(
    "INSERT INTO login_codes (email, code_hash, expires_at, attempts, sent_at) VALUES (?,?,?,0,?) " +
      "ON CONFLICT(email) DO UPDATE SET code_hash=excluded.code_hash, expires_at=excluded.expires_at, attempts=0, sent_at=excluded.sent_at",
  ).bind(email, hash, Date.now() + CODE_TTL_MS, Date.now()).run();
  await sendEmail(env, {
    to: email,
    subject: `${env.TROOP_NAME} sign-in code: ${code}`,
    text: `Your ${env.TROOP_NAME} Finances sign-in code is ${code}.\nIt expires in 10 minutes. If you didn't ask for it, ignore this email.`,
  });
}

/** Verifies a code; on success creates a session and returns its id. */
export async function verifyCode(env: Env, rawEmail: string, rawCode: string): Promise<string | null> {
  const email = normalizeEmail(rawEmail);
  const row = await env.DB.prepare("SELECT code_hash, expires_at, attempts FROM login_codes WHERE email = ?")
    .bind(email).first<{ code_hash: string; expires_at: number; attempts: number }>();
  if (!row || row.expires_at < Date.now() || row.attempts >= MAX_ATTEMPTS) return null;
  const hash = await sha256Hex(`${env.CODE_PEPPER ?? ""}:${email}:${rawCode.trim()}`);
  if (hash !== row.code_hash) {
    await env.DB.prepare("UPDATE login_codes SET attempts = attempts + 1 WHERE email = ?").bind(email).run();
    return null;
  }
  await env.DB.prepare("DELETE FROM login_codes WHERE email = ?").bind(email).run();
  if (!(await resolveUser(env, email))) return null;
  const id = newId() + newId();
  await env.DB.prepare("INSERT INTO sessions (id, email, expires_at) VALUES (?,?,?)")
    .bind(id, email, Date.now() + SESSION_DAYS * 86400_000).run();
  return id;
}

export function startSession(c: Context<AppEnv>, sessionId: string) {
  setCookie(c, SESSION_COOKIE, sessionId, {
    httpOnly: true, secure: new URL(c.req.url).protocol === "https:", sameSite: "Lax", path: "/", maxAge: SESSION_DAYS * 86400,
  });
}

export async function endSession(c: Context<AppEnv>) {
  const id = getCookie(c, SESSION_COOKIE);
  if (id) await c.env.DB.prepare("DELETE FROM sessions WHERE id = ?").bind(id).run();
  deleteCookie(c, SESSION_COOKIE, { path: "/" });
}

/** Loads the signed-in user (or null) into the request context. */
export const loadUser: MiddlewareHandler<AppEnv> = async (c, next) => {
  c.set("user", null);
  const id = getCookie(c, SESSION_COOKIE);
  if (id) {
    const s = await c.env.DB.prepare("SELECT email, expires_at FROM sessions WHERE id = ?").bind(id).first<{ email: string; expires_at: number }>();
    if (s && s.expires_at > Date.now()) c.set("user", await resolveUser(c.env, s.email));
  }
  await next();
};

export const requireUser: MiddlewareHandler<AppEnv> = async (c, next) => {
  if (!c.get("user")) return c.redirect("/login");
  await next();
};

export const requireAdmin: MiddlewareHandler<AppEnv> = async (c, next) => {
  const u = c.get("user");
  if (!u) return c.redirect("/login");
  if (!u.isAdmin) return c.text("Admins only.", 403);
  await next();
};

/** Rejects cross-site form posts. */
export const sameOrigin: MiddlewareHandler<AppEnv> = async (c, next) => {
  if (c.req.method === "POST") {
    const origin = c.req.header("origin");
    if (origin && origin !== new URL(c.req.url).origin) return c.text("Cross-site request blocked.", 403);
  }
  await next();
};

export async function purgeExpired(env: Env) {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM sessions WHERE expires_at < ?").bind(Date.now()),
    env.DB.prepare("DELETE FROM login_codes WHERE expires_at < ?").bind(Date.now()),
  ]);
}

export async function audit(env: Env, actor: string, action: string, detail = "") {
  await env.DB.prepare("INSERT INTO audit (id, at, actor, action, detail) VALUES (?,?,?,?,?)")
    .bind(newId(), nowIso(), actor, action, detail).run();
}
