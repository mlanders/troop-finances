import type { Env } from "../types";

export interface Mail { to: string; subject: string; text: string }

/** Sends through Resend when configured; otherwise prints to the Worker log (local dev, `wrangler tail`). */
export async function sendEmail(env: Env, mail: Mail): Promise<void> {
  if (!env.RESEND_API_KEY || !env.EMAIL_FROM) {
    console.log(`[email not configured] To: ${mail.to}\nSubject: ${mail.subject}\n${mail.text}`);
    return;
  }
  const res = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: { Authorization: `Bearer ${env.RESEND_API_KEY}`, "Content-Type": "application/json" },
    body: JSON.stringify({ from: env.EMAIL_FROM, to: [mail.to], subject: mail.subject, text: mail.text }),
  });
  if (!res.ok) console.error("Resend failed", res.status, await res.text());
}
