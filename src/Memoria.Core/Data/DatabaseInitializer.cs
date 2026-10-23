using Dapper;

namespace Memoria.Core.Data;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private const long TargetVersion = 7;
    private readonly SqliteConnectionFactory _factory;

    public DatabaseInitializer(SqliteConnectionFactory factory) => _factory = factory;

    public void EnsureReady()
    {
        // 마이그레이션은 쓰기이므로 단일 직렬 라이터 락 + 영속 쓰기 연결로 수행(계약 §8).
        using (DatabaseDiagnostics.EnterWrite(_factory.WriteSync, "Database.EnsureReady"))
        {
            var conn = _factory.Write;
            conn.Execute(
                "CREATE TABLE IF NOT EXISTS _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");

            var current = conn.ExecuteScalar<long>("PRAGMA user_version;");
            if (current >= TargetVersion) return;

            if (current < 1) ApplyV1(conn);
            if (current < 2) ApplyV2(conn);
            if (current < 3) ApplyV3(conn);
            if (current < 4) ApplyV4(conn);
            if (current < 5) ApplyV5(conn);
            if (current < 6) ApplyV6(conn);
            if (current < 7) ApplyV7(conn);
        }
    }

    private static void ApplyV1(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute(SchemaV1, transaction: tx);
        SeedV1(conn, tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 1;", transaction: tx);
        tx.Commit();
    }

    private static void ApplyV2(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute("ALTER TABLE notes ADD COLUMN body_format TEXT NOT NULL DEFAULT 'plain';", transaction: tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(2, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 2;", transaction: tx);
        tx.Commit();
    }

    // '자율형 공장' 고객사 표시명을 'SLD 자율형공장'으로 개명(일일업무일지/주간보고 출력 표기 통일).
    // 규칙은 client_id로 연결되어 있어 개명해도 분류 규칙은 유지된다.
    private static void ApplyV3(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute("UPDATE clients SET name = 'SLD 자율형공장' WHERE name = '자율형 공장';", transaction: tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(3, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 3;", transaction: tx);
        tx.Commit();
    }

    private static void ApplyV4(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        var requiredTables = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('notes', 'checklist_items');",
            transaction: tx);
        if (requiredTables == 2)
        {
            conn.Execute(@"
DROP TRIGGER IF EXISTS notes_au;
CREATE TRIGGER notes_au AFTER UPDATE OF title, body ON notes BEGIN
  UPDATE notes_fts
     SET title = COALESCE(new.title, ''), body = COALESCE(new.body, '')
   WHERE rowid = new.id;
END;

DROP TRIGGER IF EXISTS items_au;
CREATE TRIGGER items_au AFTER UPDATE OF text ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = new.note_id)
   WHERE rowid = new.note_id;
END;", transaction: tx);
        }
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(4, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 4;", transaction: tx);
        tx.Commit();
    }

    // 메모/업무일지가 장기간 누적되어도 가장 자주 쓰는 목록·날짜·휴지통 조회가
    // 전체 테이블 정렬로 커지지 않도록 실제 WHERE/ORDER BY 조합에 맞춘 복합 인덱스.
    private static void ApplyV5(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        var notesIndexable = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM pragma_table_info('notes') " +
            "WHERE name IN ('id','group_id','type','deleted_at','pinned','sort_order','updated_at','log_date');",
            transaction: tx) == 8;
        var itemsExists = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'checklist_items';", transaction: tx) == 1;
        var groupsExists = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'groups';", transaction: tx) == 1;

        if (notesIndexable)
            conn.Execute(@"
CREATE INDEX IF NOT EXISTS idx_notes_group_active_order
    ON notes(group_id, deleted_at, pinned DESC, sort_order, updated_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS idx_notes_checklist_date
    ON notes(type, deleted_at, log_date, id);
CREATE INDEX IF NOT EXISTS idx_notes_trash_order
    ON notes(deleted_at DESC, id DESC);", transaction: tx);
        if (itemsExists)
            conn.Execute(@"
CREATE INDEX IF NOT EXISTS idx_items_note_order
    ON checklist_items(note_id, sort_order, id);", transaction: tx);
        if (groupsExists)
            conn.Execute(@"
CREATE INDEX IF NOT EXISTS idx_groups_parent_order
    ON groups(parent_id, sort_order, id);", transaction: tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(5, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 5;", transaction: tx);
        tx.Commit();
    }

    private static void ApplyV6(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute(@"
UPDATE clients SET name = '충북테크노파크 (FEMS)' WHERE name = '충북테크놀로지파크';
UPDATE notes SET report_format = 'D' WHERE report_format = 'C';
INSERT OR IGNORE INTO settings(key, value) VALUES('report.formatD.visible', 'false');
CREATE TABLE work_entries (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
INSERT INTO _migrations(version, applied_at) VALUES(6, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
PRAGMA user_version = 6;", transaction: tx);
        tx.Commit();
    }

    private static void ApplyV7(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute(@"
INSERT INTO clients(name, sort_order, enabled)
SELECT 'SLD 도금', COALESCE(MAX(sort_order), 0) + 1, 1 FROM clients
HAVING NOT EXISTS (SELECT 1 FROM clients WHERE name = 'SLD 도금');
INSERT INTO client_rules(client_id, keyword, priority)
SELECT c.id, 'SLD 도금', COALESCE((SELECT MIN(priority) FROM client_rules), 1) - 1
FROM clients c WHERE c.name = 'SLD 도금'
AND NOT EXISTS (SELECT 1 FROM client_rules WHERE keyword = 'SLD 도금');
INSERT INTO _migrations(version, applied_at) VALUES(7, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
PRAGMA user_version = 7;", transaction: tx);
        tx.Commit();
    }

    public bool CheckIntegrity()
    {
        using var conn = _factory.Open();
        var result = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static void SeedV1(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx)
    {
        conn.Execute(SeedClientsSql, transaction: tx);
        conn.Execute(SeedRulesSql, transaction: tx);
        conn.Execute(SeedGroupsSql, transaction: tx);
        conn.Execute(SeedSettingsSql, transaction: tx);
    }

    private const string SchemaV1 = @"
CREATE TABLE groups (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL,
  parent_id   INTEGER REFERENCES groups(id),
  is_system   INTEGER NOT NULL DEFAULT 0,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  color       TEXT,
  created_at  TEXT NOT NULL
);

CREATE TABLE clients (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL,
  sort_order  INTEGER NOT NULL,
  enabled     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE notes (
  id                 INTEGER PRIMARY KEY,
  group_id           INTEGER REFERENCES groups(id) ON DELETE SET NULL,
  type               TEXT NOT NULL,
  title              TEXT,
  body               TEXT,
  log_date           TEXT,
  report_format      TEXT,
  report_week_start  TEXT,
  pinned             INTEGER NOT NULL DEFAULT 0,
  sort_order         INTEGER NOT NULL DEFAULT 0,
  deleted_at         TEXT,
  created_at         TEXT NOT NULL,
  updated_at         TEXT NOT NULL
);

CREATE TABLE checklist_items (
  id          INTEGER PRIMARY KEY,
  note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
  kind        TEXT NOT NULL,
  text        TEXT NOT NULL,
  done        INTEGER NOT NULL DEFAULT 0,
  done_at     TEXT,
  client_id   INTEGER REFERENCES clients(id) ON DELETE SET NULL,
  is_manual   INTEGER NOT NULL DEFAULT 0,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL
);

CREATE TABLE client_rules (
  id         INTEGER PRIMARY KEY,
  client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  keyword    TEXT NOT NULL,
  priority   INTEGER NOT NULL
);

CREATE TABLE settings (
  key    TEXT PRIMARY KEY,
  value  TEXT NOT NULL
);

CREATE INDEX idx_notes_group_id   ON notes(group_id);
CREATE INDEX idx_notes_log_date   ON notes(log_date);
CREATE INDEX idx_notes_deleted_at ON notes(deleted_at);
CREATE INDEX idx_notes_week       ON notes(report_week_start, report_format);
CREATE INDEX idx_items_note_id    ON checklist_items(note_id);
CREATE INDEX idx_items_client_id  ON checklist_items(client_id);

CREATE VIRTUAL TABLE notes_fts USING fts5(title, body, items);

CREATE TRIGGER notes_ai AFTER INSERT ON notes BEGIN
  INSERT INTO notes_fts(rowid, title, body, items)
  VALUES (new.id, COALESCE(new.title, ''), COALESCE(new.body, ''), '');
END;

CREATE TRIGGER notes_au AFTER UPDATE ON notes BEGIN
  UPDATE notes_fts
     SET title = COALESCE(new.title, ''), body = COALESCE(new.body, '')
   WHERE rowid = new.id;
END;

CREATE TRIGGER notes_ad AFTER DELETE ON notes BEGIN
  DELETE FROM notes_fts WHERE rowid = old.id;
END;

CREATE TRIGGER items_ai AFTER INSERT ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = new.note_id)
   WHERE rowid = new.note_id;
END;

CREATE TRIGGER items_au AFTER UPDATE ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = new.note_id)
   WHERE rowid = new.note_id;
END;

CREATE TRIGGER items_ad AFTER DELETE ON checklist_items BEGIN
  UPDATE notes_fts
     SET items = (SELECT COALESCE(GROUP_CONCAT(text, ' '), '')
                    FROM checklist_items WHERE note_id = old.note_id)
   WHERE rowid = old.note_id;
END;
";

    private const string SeedClientsSql = @"
INSERT INTO clients(name, sort_order, enabled) VALUES
  ('SLD', 1, 1),
  ('MTP', 2, 1),
  ('코모텍', 3, 1),
  ('충북테크놀로지파크', 4, 1),
  ('자율형 공장', 5, 1),
  ('카본센스', 6, 1);
";

    private const string SeedRulesSql = @"
INSERT INTO client_rules(client_id, keyword, priority)
SELECT id, '자율형공장', 1 FROM clients WHERE name = '자율형 공장'
UNION ALL SELECT id, '자율형 공장', 1 FROM clients WHERE name = '자율형 공장'
UNION ALL SELECT id, '충북', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, '충북테크놀로지파크', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, 'DL정보기술', 2 FROM clients WHERE name = '충북테크놀로지파크'
UNION ALL SELECT id, '코모텍', 3 FROM clients WHERE name = '코모텍'
UNION ALL SELECT id, 'MTP', 4 FROM clients WHERE name = 'MTP'
UNION ALL SELECT id, '머티리얼즈파크', 4 FROM clients WHERE name = 'MTP'
UNION ALL SELECT id, '카본센스', 5 FROM clients WHERE name = '카본센스'
UNION ALL SELECT id, 'SLD', 6 FROM clients WHERE name = 'SLD';
";

    private const string SeedGroupsSql = @"
INSERT INTO groups(name, is_system, sort_order, created_at) VALUES
  ('일일업무일지', 1, 0, strftime('%Y-%m-%dT%H:%M:%fZ','now')),
  ('주간보고',    1, 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
";

    private const string SeedSettingsSql = @"
INSERT INTO settings(key, value) VALUES
  ('theme.mode', 'system'),
  ('theme.preset', 'default'),
  ('report.reporterName', '이승현'),
  ('report.formatA.taskHeader', '[업무 내용]'),
  ('report.formatA.issueHeader', '[이슈]'),
  ('report.formatB.titleWord', '주간 보고'),
  ('report.formatB.issueHeader', '* 이슈사항:'),
  ('report.indent', char(9)),
  ('report.includeDoneOnly', 'false'),
  ('hotkey.newNote', 'Ctrl+Alt+N'),
  ('app.autostart', 'true'),
  ('app.closeToTray', 'true'),
  ('backup.retentionCount', '7'),
  ('trash.retentionDays', '30'),
  ('autosave.debounceMs', '500');
";
}
