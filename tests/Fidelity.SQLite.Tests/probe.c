/* Deterministic ABI probe, not a SQLite engine. No buffer is copied: bindings
   retain the exact caller pointer; column access returns that same pointer.
   The unused generated symbols are supplied by the test harness only for the
   generator's static archive symbol census and are never executed. */
#include <stddef.h>
#include <string.h>
#include <stdio.h>
typedef struct sqlite3 { int live; } sqlite3;
typedef struct sqlite3_stmt {
    int live;
    const void *blob;
    const char *text;
    int bytes;
    int text_bytes;
} sqlite3_stmt;
static sqlite3 connection;
static sqlite3_stmt statement;
static int failures;
int sqlite3_open_v2(const char *filename, sqlite3 **db, int flags, const char *vfs) {
    if (strcmp(filename, ":memory:") || flags != 6 || vfs) failures++;
    connection.live = 1; *db = &connection; return 0;
}
int sqlite3_prepare_v2(sqlite3 *db, const char *sql, int n, sqlite3_stmt **stmt, const char **tail) {
    if (db != &connection || !db->live || n != 13 || memcmp(sql, "SELECT ?1, ?2", 13) || tail) failures++;
    statement.live = 1; *stmt = &statement; return 0;
}
int sqlite3_bind_blob(sqlite3_stmt *stmt, int index, const void *data, int n, void (*destructor)(void *)) {
    if (stmt != &statement || index != 1 || n != 1 || destructor) failures++;
    stmt->blob = data; stmt->bytes = n; return 0;
}
int sqlite3_bind_text(sqlite3_stmt *stmt, int index, const char *text, int n, void (*destructor)(void *)) {
    if (stmt != &statement || index != 2 || n != 2 || destructor) failures++;
    stmt->text = text; stmt->text_bytes = n; return 0;
}
int sqlite3_step(sqlite3_stmt *stmt) { return stmt->live ? 100 : 21; }
const void *sqlite3_column_blob(sqlite3_stmt *stmt, int column) { return column == 0 ? stmt->blob : NULL; }
const unsigned char *sqlite3_column_text(sqlite3_stmt *stmt, int column) { return column == 1 ? (const unsigned char *)stmt->text : NULL; }
int sqlite3_column_bytes(sqlite3_stmt *stmt, int column) { return column == 0 ? stmt->bytes : stmt->text_bytes; }
int sqlite3_reset(sqlite3_stmt *stmt) { return stmt->live ? 0 : 21; }
int sqlite3_clear_bindings(sqlite3_stmt *stmt) {
    if (!stmt->blob || !stmt->text || *(const unsigned char *)stmt->blob != 65 || stmt->text[0] != 'O') failures++;
    stmt->blob = NULL; stmt->text = NULL; return 0;
}
int sqlite3_finalize(sqlite3_stmt *stmt) {
    if (!stmt || !stmt->live) failures++;
    if (stmt) { stmt->live = 0; stmt->blob = NULL; stmt->text = NULL; }
    return 0;
}
int sqlite3_close_v2(sqlite3 *db) {
    if (db != &connection || !db->live || statement.live) failures++;
    if (db) db->live = 0;
    return failures;
}

/* Owned-return adapter: the compiler sees the real open/close protocol without
   requiring SQLite's unsupported pointer-reference output parameter. */
sqlite3 *probe_open_owned(int flags) {
    sqlite3 *db = NULL;
    return sqlite3_open_v2(":memory:", &db, flags, NULL) == 0 ? db : NULL;
}
sqlite3 *sqlite3_db_handle(sqlite3_stmt *stmt) {
    if (stmt != &statement || !stmt->live) failures++;
    return &connection;
}
int probe_invoke(int (*callback)(sqlite3_stmt *, sqlite3 *)) {
    sqlite3 *db = NULL;
    sqlite3_stmt *stmt = NULL;
    if (sqlite3_open_v2(":memory:", &db, 6, NULL) != 0) return 10;
    if (sqlite3_prepare_v2(db, "SELECT ?1, ?2", 13, &stmt, NULL) != 0) return 11;
    int result = callback(stmt, sqlite3_db_handle(stmt));
    if (failures || connection.live || statement.live) return 12;
    if (result == 0) puts("sqlite:zero-copy:ok");
    return result;
}

int probe_close_owned(sqlite3 *db) { return sqlite3_close_v2(db); }
