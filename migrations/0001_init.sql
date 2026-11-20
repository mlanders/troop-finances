-- All money is stored as integer cents.
CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE admins (
  email TEXT PRIMARY KEY,
  name TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL
);

CREATE TABLE guardians (
  id TEXT PRIMARY KEY,
  full_name TEXT NOT NULL,
  email TEXT NOT NULL UNIQUE,
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);

CREATE TABLE scouts (
  id TEXT PRIMARY KEY,
  full_name TEXT NOT NULL,
  rank TEXT,
  patrol TEXT,
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);

CREATE TABLE guardian_scouts (
  guardian_id TEXT NOT NULL REFERENCES guardians(id) ON DELETE CASCADE,
  scout_id TEXT NOT NULL REFERENCES scouts(id) ON DELETE CASCADE,
  PRIMARY KEY (guardian_id, scout_id)
);

-- A scout's account balance is SUM(amount_cents): credits positive, debits negative.
CREATE TABLE ledger (
  id TEXT PRIMARY KEY,
  scout_id TEXT NOT NULL REFERENCES scouts(id),
  amount_cents INTEGER NOT NULL,
  source_type TEXT NOT NULL,
  source_id TEXT NOT NULL DEFAULT '',
  description TEXT NOT NULL,
  created_by TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE INDEX ledger_scout ON ledger(scout_id, created_at);

CREATE TABLE invoices (
  id TEXT PRIMARY KEY,
  scout_id TEXT NOT NULL REFERENCES scouts(id),
  description TEXT NOT NULL,
  total_cents INTEGER NOT NULL,
  applied_cents INTEGER NOT NULL DEFAULT 0,
  due_cents INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'Outstanding',
  issued_at TEXT NOT NULL,
  issued_by TEXT NOT NULL,
  due_date TEXT,
  paid_at TEXT,
  void_reason TEXT,
  voided_at TEXT,
  voided_by TEXT
);
CREATE INDEX invoices_scout ON invoices(scout_id);
CREATE INDEX invoices_status ON invoices(status);

CREATE TABLE invoice_payments (
  id TEXT PRIMARY KEY,
  invoice_id TEXT NOT NULL REFERENCES invoices(id),
  amount_cents INTEGER NOT NULL,
  method TEXT NOT NULL,
  reference TEXT NOT NULL DEFAULT '',
  recorded_by TEXT NOT NULL,
  paid_at TEXT NOT NULL
);
CREATE INDEX payments_invoice ON invoice_payments(invoice_id);

CREATE TABLE fundraisers (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  event_date TEXT NOT NULL,
  total_raised_cents INTEGER NOT NULL DEFAULT 0,
  total_expenses_cents INTEGER NOT NULL DEFAULT 0,
  goal_cents INTEGER,
  description TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'Open',
  created_at TEXT NOT NULL,
  created_by TEXT NOT NULL
);

CREATE TABLE reimbursements (
  id TEXT PRIMARY KEY,
  guardian_id TEXT NOT NULL REFERENCES guardians(id),
  submitter_name TEXT NOT NULL,
  submitter_email TEXT NOT NULL,
  scout_id TEXT REFERENCES scouts(id),
  amount_cents INTEGER NOT NULL,
  description TEXT NOT NULL,
  receipt_key TEXT NOT NULL,
  receipt_type TEXT NOT NULL,
  receipt_name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'Pending',
  submitted_at TEXT NOT NULL,
  reviewed_at TEXT,
  reviewed_by TEXT,
  review_notes TEXT,
  paid_at TEXT,
  paid_by TEXT,
  payment_note TEXT
);
CREATE INDEX reimb_status ON reimbursements(status);
CREATE INDEX reimb_guardian ON reimbursements(guardian_id);

CREATE TABLE login_codes (
  email TEXT PRIMARY KEY,
  code_hash TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  sent_at INTEGER NOT NULL
);

CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  email TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);

CREATE TABLE audit (
  id TEXT PRIMARY KEY,
  at TEXT NOT NULL,
  actor TEXT NOT NULL,
  action TEXT NOT NULL,
  detail TEXT NOT NULL DEFAULT ''
);
CREATE INDEX audit_at ON audit(at);
