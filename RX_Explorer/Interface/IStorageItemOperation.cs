﻿using SharedLibrary;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace RX_Explorer.Interface
{
    public interface IStorageItemOperation
    {
        public Task MoveAsync(string DirectoryPath, string NewName = null, CollisionOptions Option = CollisionOptions.Skip, bool SkipOperationRecord = false, CancellationToken CancelToken = default, ProgressChangedEventHandler ProgressHandler = null);
        public Task CopyAsync(string DirectoryPath, CollisionOptions Option = CollisionOptions.Skip, bool SkipOperationRecord = false, CancellationToken CancelToken = default, ProgressChangedEventHandler ProgressHandler = null);
        public Task DeleteAsync(bool PermanentDelete, bool SkipOperationRecord = false, CancellationToken CancelToken = default, ProgressChangedEventHandler ProgressHandler = null);
        public Task<string> RenameAsync(string DesireName, bool SkipOperationRecord = false, CancellationToken CancelToken = default);
    }
}
