using System;
using System.IO;

namespace GitSiteSyncer.Utilities
{
    public class LockHelper : IDisposable
    {
        private readonly string _lockFilePath;
        private FileStream _lockFileStream;

        public LockHelper(string lockFilePath)
        {
            _lockFilePath = lockFilePath;
        }

        public bool TryAcquireLock()
        {
            try
            {
                _lockFileStream = new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                // Lock file is in use by another process
                return false;
            }
        }

        public void ReleaseLock()
        {
            _lockFileStream?.Close();
            if (File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
            }
        }

        public void Dispose()
        {
            ReleaseLock();
        }
    }
}
