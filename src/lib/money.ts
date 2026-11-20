/** Money helpers. Amounts are integer cents everywhere except at the edges (forms, display). */

const fmt = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

export function formatCents(cents: number): string {
  return fmt.format(cents / 100);
}

/** Parses "12", "12.5", "$1,234.56" into cents. Returns null when it isn't a valid non-negative amount. */
export function parseAmount(input: string | null | undefined): number | null {
  if (input == null) return null;
  const s = input.trim().replace(/^\$/, "").replace(/,/g, "");
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return null;
  const [whole, frac = ""] = s.split(".");
  return Number(whole) * 100 + Number((frac + "00").slice(0, 2));
}

/** Splits `totalCents` into `parts` shares that differ by at most one cent and sum exactly to the total. */
export function splitEvenly(totalCents: number, parts: number): number[] {
  if (parts <= 0) return [];
  const base = Math.floor(totalCents / parts);
  const extra = totalCents - base * parts;
  return Array.from({ length: parts }, (_, i) => base + (i < extra ? 1 : 0));
}

/** How a new invoice is funded: scout's account balance first, remainder from the family. */
export function planInvoice(totalCents: number, balanceCents: number): { appliedCents: number; dueCents: number } {
  const appliedCents = Math.max(0, Math.min(totalCents, balanceCents));
  return { appliedCents, dueCents: totalCents - appliedCents };
}
