import type { Env } from "../types";
import { newId, nowIso } from "./util";

export async function scoutBalance(env: Env, scoutId: string): Promise<number> {
  const r = await env.DB.prepare("SELECT COALESCE(SUM(amount_cents),0) AS b FROM ledger WHERE scout_id = ?").bind(scoutId).first<{ b: number }>();
  return r?.b ?? 0;
}

export function ledgerStmt(env: Env, e: { scoutId: string; cents: number; type: string; sourceId?: string; description: string; by: string }) {
  return env.DB.prepare(
    "INSERT INTO ledger (id, scout_id, amount_cents, source_type, source_id, description, created_by, created_at) VALUES (?,?,?,?,?,?,?,?)",
  ).bind(newId(), e.scoutId, e.cents, e.type, e.sourceId ?? "", e.description, e.by, nowIso());
}
