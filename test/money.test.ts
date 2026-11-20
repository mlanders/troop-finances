import { describe, expect, it } from "vitest";
import { formatCents, parseAmount, planInvoice, splitEvenly } from "../src/lib/money";

describe("parseAmount", () => {
  it("parses common forms", () => {
    expect(parseAmount("12")).toBe(1200);
    expect(parseAmount("12.5")).toBe(1250);
    expect(parseAmount("$1,234.56")).toBe(123456);
    expect(parseAmount(" 0.07 ")).toBe(7);
  });
  it("rejects junk and negatives", () => {
    for (const bad of ["", "abc", "-5", "1.234", "1e3", null, undefined]) expect(parseAmount(bad as any)).toBeNull();
  });
});

describe("splitEvenly", () => {
  it("sums exactly and spreads leftovers", () => {
    expect(splitEvenly(100, 3)).toEqual([34, 33, 33]);
    expect(splitEvenly(1, 4)).toEqual([1, 0, 0, 0]);
    expect(splitEvenly(10000, 7).reduce((a, b) => a + b, 0)).toBe(10000);
    expect(splitEvenly(5, 0)).toEqual([]);
  });
});

describe("planInvoice", () => {
  it("applies available balance first", () => {
    expect(planInvoice(10000, 2500)).toEqual({ appliedCents: 2500, dueCents: 7500 });
    expect(planInvoice(10000, 99999)).toEqual({ appliedCents: 10000, dueCents: 0 });
    expect(planInvoice(10000, -500)).toEqual({ appliedCents: 0, dueCents: 10000 });
  });
});

describe("formatCents", () => {
  it("formats", () => expect(formatCents(123456)).toBe("$1,234.56"));
});
