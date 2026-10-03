using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace WinAI.Data.Database
{
    public interface ISqliteRow
    {
        string GetString(string columnName);
        int GetInt32(string columnName);
        long GetInt64(string columnName);
        bool GetBoolean(string columnName);
        DateTime GetDateTimeUtc(string columnName);
        bool IsDBNull(string columnName);
    }

    /// <summary>
    /// Thread-safe, async-capable SQLite database engine utilizing the built-in
    /// Windows 10 / Windows 10 Mobile OS 'winsqlite3.dll'.
    /// Requires zero external NuGet packages and has guaranteed ARM native compatibility.
    /// </summary>
    public sealed class WinAIDatabase : IDisposable
    {
        private const string SqliteLibrary = "winsqlite3";
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;

        private const int SQLITE_OPEN_READWRITE = 0x00000002;
        private const int SQLITE_OPEN_CREATE = 0x00000004;
        private const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

        #region Native SQLite P/Invoke Definitions

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close")]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_null")]
        private static extern int sqlite3_bind_null(IntPtr stmt, int index);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int")]
        private static extern int sqlite3_bind_int(IntPtr stmt, int index, int value);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int64")]
        private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
        private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] val, int len, IntPtr destructor);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_name")]
        private static extern IntPtr sqlite3_column_name(IntPtr stmt, int col);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_type")]
        private static extern int sqlite3_column_type(IntPtr stmt, int col);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int")]
        private static extern int sqlite3_column_int(IntPtr stmt, int col);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
        private static extern long sqlite3_column_int64(IntPtr stmt, int col);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        [DllImport(SqliteLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_changes")]
        private static extern int sqlite3_changes(IntPtr db);

        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        #endregion

        private readonly string _databasePath;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<int> _reentrancyDepth = new AsyncLocal<int>();
        private IntPtr _dbHandle = IntPtr.Zero;
        private bool _isDisposed;

        public string DatabasePath => _databasePath;

        public WinAIDatabase(string customPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                _databasePath = customPath;
            }
            else
            {
                try
                {
                    _databasePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "winai_conversations.db");
                }
                catch
                {
                    _databasePath = Path.Combine(Path.GetTempPath(), "winai_conversations.db");
                }
            }
        }

        public async Task OpenAsync()
        {
            if (_reentrancyDepth.Value > 0)
            {
                EnsureOpen();
                return;
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            _reentrancyDepth.Value++;
            try
            {
                EnsureOpen();
            }
            finally
            {
                _reentrancyDepth.Value--;
                _gate.Release();
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params object[] parameters)
        {
            if (_reentrancyDepth.Value > 0)
            {
                EnsureOpen();
                return ExecuteNonQueryInternal(sql, parameters);
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            _reentrancyDepth.Value++;
            try
            {
                EnsureOpen();
                return ExecuteNonQueryInternal(sql, parameters);
            }
            finally
            {
                _reentrancyDepth.Value--;
                _gate.Release();
            }
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, params object[] parameters)
        {
            if (_reentrancyDepth.Value > 0)
            {
                EnsureOpen();
                return ExecuteScalarInternal<T>(sql, parameters);
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            _reentrancyDepth.Value++;
            try
            {
                EnsureOpen();
                return ExecuteScalarInternal<T>(sql, parameters);
            }
            finally
            {
                _reentrancyDepth.Value--;
                _gate.Release();
            }
        }

        public async Task<List<T>> QueryAsync<T>(string sql, Func<ISqliteRow, T> mapper, params object[] parameters)
        {
            if (_reentrancyDepth.Value > 0)
            {
                EnsureOpen();
                return QueryInternal(sql, mapper, parameters);
            }

            await _gate.WaitAsync().ConfigureAwait(false);
            _reentrancyDepth.Value++;
            try
            {
                EnsureOpen();
                return QueryInternal(sql, mapper, parameters);
            }
            finally
            {
                _reentrancyDepth.Value--;
                _gate.Release();
            }
        }

        public async Task RunInTransactionAsync(Func<WinAIDatabase, Task> action)
        {
            bool acquiredLock = false;
            if (_reentrancyDepth.Value == 0)
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                acquiredLock = true;
            }

            _reentrancyDepth.Value++;
            int depth = _reentrancyDepth.Value;

            try
            {
                EnsureOpen();
                bool isNested = !acquiredLock;
                string savepointName = $"sp_tx_{depth}";

                if (isNested)
                {
                    ExecuteNonQueryInternal($"SAVEPOINT {savepointName};");
                }
                else
                {
                    ExecuteNonQueryInternal("BEGIN IMMEDIATE TRANSACTION;");
                }

                try
                {
                    await action(this).ConfigureAwait(false);

                    if (isNested)
                    {
                        ExecuteNonQueryInternal($"RELEASE SAVEPOINT {savepointName};");
                    }
                    else
                    {
                        ExecuteNonQueryInternal("COMMIT;");
                    }
                }
                catch
                {
                    if (isNested)
                    {
                        try { ExecuteNonQueryInternal($"ROLLBACK TO SAVEPOINT {savepointName};"); } catch { }
                    }
                    else
                    {
                        try { ExecuteNonQueryInternal("ROLLBACK;"); } catch { }
                    }
                    throw;
                }
            }
            finally
            {
                _reentrancyDepth.Value--;
                if (acquiredLock)
                {
                    _gate.Release();
                }
            }
        }

        public async Task<int> GetSchemaVersionAsync()
        {
            return await ExecuteScalarAsync<int>("PRAGMA user_version;").ConfigureAwait(false);
        }

        public async Task SetSchemaVersionAsync(int version)
        {
            await ExecuteNonQueryAsync($"PRAGMA user_version = {version};").ConfigureAwait(false);
        }

        public async Task<bool> CheckIntegrityAsync()
        {
            try
            {
                string status = await ExecuteScalarAsync<string>("PRAGMA quick_check(1);").ConfigureAwait(false);
                return string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #region Internal Synchronous Execution Methods (Assumes lock held)

        public int ExecuteNonQueryInternal(string sql, params object[] parameters)
        {
            IntPtr stmt = PrepareStatement(sql, parameters);
            try
            {
                int stepResult = sqlite3_step(stmt);
                if (stepResult != SQLITE_DONE && stepResult != SQLITE_ROW && stepResult != SQLITE_OK)
                {
                    throw new InvalidOperationException($"Error executing query ({stepResult}): {GetErrorMessage(_dbHandle)}");
                }
                return sqlite3_changes(_dbHandle);
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        public T ExecuteScalarInternal<T>(string sql, params object[] parameters)
        {
            IntPtr stmt = PrepareStatement(sql, parameters);
            try
            {
                int stepResult = sqlite3_step(stmt);
                if (stepResult == SQLITE_ROW)
                {
                    var row = new SqliteRowReader(stmt);
                    object val = row.GetValueByIndex(0);
                    if (val == null || val == DBNull.Value) return default(T);
                    return (T)Convert.ChangeType(val, typeof(T), CultureInfo.InvariantCulture);
                }
                return default(T);
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        public List<T> QueryInternal<T>(string sql, Func<ISqliteRow, T> mapper, params object[] parameters)
        {
            IntPtr stmt = PrepareStatement(sql, parameters);
            try
            {
                var list = new List<T>();
                var row = new SqliteRowReader(stmt);

                while (sqlite3_step(stmt) == SQLITE_ROW)
                {
                    T mapped = mapper(row);
                    list.Add(mapped);
                }

                return list;
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        private IntPtr PrepareStatement(string sql, object[] parameters)
        {
            byte[] sqlBytes = GetNullTerminatedUtf8(sql);
            int res = sqlite3_prepare_v2(_dbHandle, sqlBytes, sqlBytes.Length, out IntPtr stmt, IntPtr.Zero);
            if (res != SQLITE_OK)
            {
                string err = GetErrorMessage(_dbHandle);
                throw new InvalidOperationException($"Failed to prepare SQLite statement '{sql}': {err}");
            }

            if (parameters != null && parameters.Length > 0)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    int paramIndex = i + 1;
                    object val = parameters[i];

                    if (val == null || val == DBNull.Value)
                    {
                        sqlite3_bind_null(stmt, paramIndex);
                    }
                    else if (val is int intVal)
                    {
                        sqlite3_bind_int(stmt, paramIndex, intVal);
                    }
                    else if (val is long longVal)
                    {
                        sqlite3_bind_int64(stmt, paramIndex, longVal);
                    }
                    else if (val is bool boolVal)
                    {
                        sqlite3_bind_int(stmt, paramIndex, boolVal ? 1 : 0);
                    }
                    else if (val is DateTime dtVal)
                    {
                        byte[] textBytes = GetNullTerminatedUtf8(dtVal.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                        sqlite3_bind_text(stmt, paramIndex, textBytes, textBytes.Length - 1, SQLITE_TRANSIENT);
                    }
                    else
                    {
                        byte[] textBytes = GetNullTerminatedUtf8(val.ToString());
                        sqlite3_bind_text(stmt, paramIndex, textBytes, textBytes.Length - 1, SQLITE_TRANSIENT);
                    }
                }
            }

            return stmt;
        }

        #endregion

        #region Helpers

        private void EnsureOpen()
        {
            if (_dbHandle != IntPtr.Zero) return;

            try
            {
                OpenDatabaseInternal();
            }
            catch (Exception)
            {
                // In the event of SQLite corruption or unreadable state, isolate file and recover cleanly
                RecoverCorruptedDatabase();
                OpenDatabaseInternal();
            }
        }

        private void OpenDatabaseInternal()
        {
            byte[] pathBytes = GetNullTerminatedUtf8(_databasePath);
            int flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX;
            int result = sqlite3_open_v2(pathBytes, out _dbHandle, flags, IntPtr.Zero);

            if (result != SQLITE_OK)
            {
                string err = GetErrorMessage(_dbHandle);
                if (_dbHandle != IntPtr.Zero)
                {
                    sqlite3_close(_dbHandle);
                    _dbHandle = IntPtr.Zero;
                }
                throw new InvalidOperationException($"Unable to open SQLite database at '{_databasePath}' ({result}): {err}");
            }

            // High-reliability settings: Foreign keys, WAL journal mode, Normal sync, and busy timeout
            ExecuteNonQueryInternal("PRAGMA foreign_keys = ON;");
            ExecuteNonQueryInternal("PRAGMA journal_mode = WAL;");
            ExecuteNonQueryInternal("PRAGMA synchronous = NORMAL;");
            ExecuteNonQueryInternal("PRAGMA busy_timeout = 5000;");

            // Verify integrity
            IntPtr stmt = IntPtr.Zero;
            try
            {
                byte[] checkSql = GetNullTerminatedUtf8("PRAGMA quick_check(1);");
                if (sqlite3_prepare_v2(_dbHandle, checkSql, checkSql.Length, out stmt, IntPtr.Zero) == SQLITE_OK)
                {
                    if (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        IntPtr txt = sqlite3_column_text(stmt, 0);
                        string status = Utf8ToString(txt);
                        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"SQLite corruption detected on quick_check: {status}");
                        }
                    }
                }
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    sqlite3_finalize(stmt);
                }
            }
        }

        private void RecoverCorruptedDatabase()
        {
            try
            {
                if (_dbHandle != IntPtr.Zero)
                {
                    sqlite3_close(_dbHandle);
                    _dbHandle = IntPtr.Zero;
                }

                if (File.Exists(_databasePath))
                {
                    string backupPath = _databasePath + ".corrupt_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                    File.Move(_databasePath, backupPath);

                    string walPath = _databasePath + "-wal";
                    if (File.Exists(walPath))
                    {
                        try { File.Move(walPath, backupPath + "-wal"); } catch { }
                    }

                    string shmPath = _databasePath + "-shm";
                    if (File.Exists(shmPath))
                    {
                        try { File.Move(shmPath, backupPath + "-shm"); } catch { }
                    }
                }
            }
            catch
            {
                // Best effort isolation; allow fresh creation attempt
            }
        }

        private static byte[] GetNullTerminatedUtf8(string str)
        {
            if (str == null) str = string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            byte[] result = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            result[bytes.Length] = 0;
            return result;
        }

        private static string GetErrorMessage(IntPtr db)
        {
            if (db == IntPtr.Zero) return "Handle is null";
            IntPtr msgPtr = sqlite3_errmsg(db);
            return Utf8ToString(msgPtr);
        }

        private static string Utf8ToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _gate.Wait();
            try
            {
                if (_dbHandle != IntPtr.Zero)
                {
                    sqlite3_close(_dbHandle);
                    _dbHandle = IntPtr.Zero;
                }
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }

        #endregion

        #region SqliteRowReader Class

        private sealed class SqliteRowReader : ISqliteRow
        {
            private readonly IntPtr _stmt;
            private Dictionary<string, int> _columnIndices;

            public SqliteRowReader(IntPtr stmt)
            {
                _stmt = stmt;
            }

            private int GetColIndex(string name)
            {
                if (_columnIndices == null)
                {
                    _columnIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    int count = sqlite3_column_count(_stmt);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr namePtr = sqlite3_column_name(_stmt, i);
                        string colName = Utf8ToString(namePtr);
                        _columnIndices[colName] = i;
                    }
                }

                if (_columnIndices.TryGetValue(name, out int index))
                {
                    return index;
                }
                throw new ArgumentException($"Column '{name}' was not found in SQLite result row.");
            }

            public string GetString(string columnName)
            {
                int col = GetColIndex(columnName);
                if (sqlite3_column_type(_stmt, col) == 5) return string.Empty; // SQLITE_NULL
                IntPtr textPtr = sqlite3_column_text(_stmt, col);
                return Utf8ToString(textPtr);
            }

            public int GetInt32(string columnName)
            {
                int col = GetColIndex(columnName);
                return sqlite3_column_int(_stmt, col);
            }

            public long GetInt64(string columnName)
            {
                int col = GetColIndex(columnName);
                return sqlite3_column_int64(_stmt, col);
            }

            public bool GetBoolean(string columnName)
            {
                return GetInt32(columnName) != 0;
            }

            public DateTime GetDateTimeUtc(string columnName)
            {
                string text = GetString(columnName);
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime dt))
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
                return DateTime.UtcNow;
            }

            public bool IsDBNull(string columnName)
            {
                int col = GetColIndex(columnName);
                return sqlite3_column_type(_stmt, col) == 5; // SQLITE_NULL
            }

            public object GetValueByIndex(int col)
            {
                int type = sqlite3_column_type(_stmt, col);
                switch (type)
                {
                    case 1: // SQLITE_INTEGER
                        return sqlite3_column_int64(_stmt, col);
                    case 3: // SQLITE_TEXT
                        return Utf8ToString(sqlite3_column_text(_stmt, col));
                    case 5: // SQLITE_NULL
                        return DBNull.Value;
                    default:
                        return Utf8ToString(sqlite3_column_text(_stmt, col));
                }
            }
        }

        #endregion
    }
}
