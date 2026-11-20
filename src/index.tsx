import { Hono } from "hono";
import type { AppEnv, Env } from "./types";
import { loadUser, purgeExpired, requireAdmin, sameOrigin } from "./lib/auth";
import { publicRoutes } from "./routes/public";
import { portalRoutes } from "./routes/portal";
import { peopleRoutes } from "./routes/admin-people";
import { moneyRoutes } from "./routes/admin-money";

const app = new Hono<AppEnv>();

app.use("*", sameOrigin);
app.use("*", loadUser);
app.use("*", async (c, next) => {
  await next();
  c.header("X-Frame-Options", "DENY");
  c.header("Referrer-Policy", "same-origin");
});

app.route("/", publicRoutes);
app.route("/portal", portalRoutes);

const admin = new Hono<AppEnv>();
admin.use("*", requireAdmin);
admin.route("/", peopleRoutes);
admin.route("/", moneyRoutes);
app.route("/admin", admin);

app.notFound((c) => c.text("Not found", 404));
app.onError((err, c) => {
  console.error(err);
  return c.text("Something went wrong.", 500);
});

export default {
  fetch: app.fetch,
  // Nightly housekeeping (see "triggers" in wrangler.jsonc).
  scheduled: (_e: ScheduledController, env: Env, ctx: ExecutionContext) => { ctx.waitUntil(purgeExpired(env)); },
} satisfies ExportedHandler<Env>;
