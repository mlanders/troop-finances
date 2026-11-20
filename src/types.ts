export interface Env {
  DB: D1Database;
  RECEIPTS: R2Bucket;
  TROOP_NAME: string;
  ADMIN_EMAILS: string;
  EMAIL_FROM?: string;
  RESEND_API_KEY?: string;
  CODE_PEPPER?: string;
}

export interface User {
  email: string;
  isAdmin: boolean;
  guardianId: string | null;
  name: string;
}

export type AppEnv = { Bindings: Env; Variables: { user: User | null } };
