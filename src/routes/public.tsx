import { Hono } from "hono";
import type { AppEnv } from "../types";
import { endSession, requestCode, startSession, verifyCode } from "../lib/auth";
import { flashRedirect, formBody, page } from "../lib/page";

export const publicRoutes = new Hono<AppEnv>();

publicRoutes.get("/", (c) => {
  const u = c.get("user");
  if (!u) return c.redirect("/login");
  return c.redirect(u.isAdmin ? "/admin" : "/portal");
});

publicRoutes.get("/login", (c) =>
  page(c, "Sign in", (
    <form method="post" action="/login" class="card narrow login">
      <p class="muted">Enter the email address the troop has on file. We'll email you a 6-digit code.</p>
      <label for="email">Email</label>
      <input id="email" name="email" type="email" autocomplete="email" required autofocus />
      <p><button>Email me a code</button></p>
    </form>
  )),
);

publicRoutes.post("/login", async (c) => {
  const f = await formBody(c);
  await requestCode(c.env, f.email ?? "");
  const email = encodeURIComponent((f.email ?? "").trim());
  return flashRedirect(c, `/login/verify?email=${email}`, "If that address is on file, a code is on its way.");
});

publicRoutes.get("/login/verify", (c) => {
  const email = c.req.query("email") ?? "";
  return page(c, "Enter your code", (
    <form method="post" action="/login/verify" class="card narrow login">
      <input type="hidden" name="email" value={email} />
      <p class="muted">Code sent to <b>{email}</b>. It expires in 10 minutes.</p>
      <label for="code">6-digit code</label>
      <input id="code" name="code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength={6} required autofocus />
      <p><button>Sign in</button> <a class="btn secondary" href="/login">Start over</a></p>
    </form>
  ));
});

publicRoutes.post("/login/verify", async (c) => {
  const f = await formBody(c);
  const session = await verifyCode(c.env, f.email ?? "", f.code ?? "");
  if (!session) return flashRedirect(c, `/login/verify?email=${encodeURIComponent(f.email ?? "")}`, "That code didn't work. Check it and try again, or start over.", "err");
  startSession(c, session);
  return c.redirect("/");
});

publicRoutes.post("/logout", async (c) => {
  await endSession(c);
  return c.redirect("/login");
});

publicRoutes.get("/healthz", (c) => c.text("ok"));
