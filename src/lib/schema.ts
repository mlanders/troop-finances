import schema from "../../migrations/0001_init.sql";
import type { Env } from "../types";

let ready: Promise<void> | null = null;

/**
 * One-click deploys run a plain `wrangler deploy`, which doesn't apply D1 migrations. So the Worker
 * creates its tables itself the first time it runs. The migration uses IF NOT EXISTS throughout,
 * which also keeps `wrangler d1 migrations apply` safe to run afterwards.
 */
export function ensureSchema(env: Env): Promise<void> {
  ready ??= (async () => {
    const hit = await env.DB.prepare("SELECT 1 AS ok FROM sqlite_master WHERE type='table' AND name='audit'").first();
    if (hit) return;
    const statements = schema.split(";\n").map((s) => s.replace(/^\s*--.*$/gm, "").trim()).filter(Boolean);
    await env.DB.batch(statements.map((s) => env.DB.prepare(s)));
  })().catch((e) => { ready = null; throw e; });
  return ready;
}
