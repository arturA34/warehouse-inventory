CREATE TABLE products (
  id TEXT PRIMARY KEY,
  sku TEXT NOT NULL,
  sku_key TEXT NOT NULL UNIQUE,
  barcode TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  unit TEXT NOT NULL,
  is_active INTEGER NOT NULL
);

CREATE TABLE warehouses (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  name_key TEXT NOT NULL UNIQUE,
  is_active INTEGER NOT NULL
);

CREATE TABLE users (
  id TEXT PRIMARY KEY,
  login TEXT NOT NULL,
  login_key TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role INTEGER NOT NULL,
  failed_attempts INTEGER NOT NULL,
  is_enabled INTEGER NOT NULL
);

CREATE TABLE stock (
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  quantity TEXT NOT NULL,
  value TEXT NOT NULL,
  PRIMARY KEY (warehouse_id, product_id)
);

CREATE TABLE minimums (
  id TEXT PRIMARY KEY,
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  quantity TEXT NOT NULL,
  UNIQUE (warehouse_id, product_id)
);

CREATE TABLE documents (
  id TEXT PRIMARY KEY,
  kind INTEGER NOT NULL,
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  destination_id TEXT REFERENCES warehouses(id),
  inventory_id TEXT,
  status INTEGER NOT NULL,
  number INTEGER UNIQUE,
  created_at INTEGER NOT NULL,
  posted_at INTEGER,
  reversed_at INTEGER
);

CREATE TABLE document_lines (
  document_id TEXT NOT NULL REFERENCES documents(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  quantity TEXT NOT NULL,
  unit_cost TEXT,
  PRIMARY KEY (document_id, product_id)
);

CREATE TABLE movements (
  id TEXT PRIMARY KEY,
  document_id TEXT NOT NULL REFERENCES documents(id),
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  quantity TEXT NOT NULL,
  value TEXT NOT NULL,
  occurred_at INTEGER NOT NULL,
  reverses_movement_id TEXT REFERENCES movements(id)
);
CREATE INDEX ix_movements_document ON movements(document_id);
CREATE INDEX ix_movements_product ON movements(product_id);
CREATE INDEX ix_movements_time ON movements(occurred_at);

CREATE TABLE inventories (
  id TEXT PRIMARY KEY,
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  status INTEGER NOT NULL,
  started_at INTEGER NOT NULL,
  finished_at INTEGER
);

CREATE TABLE inventory_lines (
  inventory_id TEXT NOT NULL REFERENCES inventories(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  book_quantity TEXT NOT NULL,
  book_value TEXT NOT NULL,
  actual_quantity TEXT,
  surplus_unit_cost TEXT,
  PRIMARY KEY (inventory_id, product_id)
);

CREATE TABLE inventory_adjustments (
  inventory_id TEXT NOT NULL REFERENCES inventories(id),
  position INTEGER NOT NULL,
  document_id TEXT NOT NULL REFERENCES documents(id),
  PRIMARY KEY (inventory_id, position)
);


CREATE TABLE inventory_locks (
  warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
  product_id TEXT NOT NULL REFERENCES products(id),
  inventory_id TEXT NOT NULL REFERENCES inventories(id),
  PRIMARY KEY (warehouse_id, product_id)
);

CREATE TABLE audit (
  id TEXT PRIMARY KEY,
  actor_id TEXT,
  action TEXT NOT NULL,
  entity_id TEXT,
  occurred_at INTEGER NOT NULL
);
CREATE INDEX ix_audit_time ON audit(occurred_at);

CREATE TABLE counters (
  name TEXT PRIMARY KEY,
  value INTEGER NOT NULL
);
INSERT INTO counters (name, value) VALUES ('document_number', 0);


CREATE TRIGGER movements_no_update BEFORE UPDATE ON movements
BEGIN SELECT RAISE(ABORT, 'append-only'); END;
CREATE TRIGGER movements_no_delete BEFORE DELETE ON movements
BEGIN SELECT RAISE(ABORT, 'append-only'); END;
CREATE TRIGGER audit_no_update BEFORE UPDATE ON audit
BEGIN SELECT RAISE(ABORT, 'append-only'); END;
CREATE TRIGGER audit_no_delete BEFORE DELETE ON audit
BEGIN SELECT RAISE(ABORT, 'append-only'); END;