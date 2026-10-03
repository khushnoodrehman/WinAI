using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinAI.Data.Database
{
    /// <summary>
    /// Handles database startup lifecycle, schema initialization, and migration execution.
    /// Ensures that local conversation memory is ready before application operations.
    /// </summary>
    public static class DatabaseInitializer
    {
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private static WinAIDatabase _instance;
        private static bool _isInitialized;

        public static WinAIDatabase Database
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WinAIDatabase();
                }
                return _instance;
            }
        }

        public static async Task<WinAIDatabase> InitializeAsync(string customPath = null)
        {
            if (_isInitialized && _instance != null && string.IsNullOrEmpty(customPath))
            {
                return _instance;
            }

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isInitialized && _instance != null && string.IsNullOrEmpty(customPath))
                {
                    return _instance;
                }

                if (!string.IsNullOrEmpty(customPath))
                {
                    var customDb = new WinAIDatabase(customPath);
                    await customDb.OpenAsync().ConfigureAwait(false);
                    await DatabaseMigrations.ApplyMigrationsAsync(customDb).ConfigureAwait(false);
                    return customDb;
                }

                _instance = new WinAIDatabase();
                await _instance.OpenAsync().ConfigureAwait(false);
                await DatabaseMigrations.ApplyMigrationsAsync(_instance).ConfigureAwait(false);

                _isInitialized = true;
                return _instance;
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
