﻿using RX_Explorer.Interface;
using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace RX_Explorer.Class
{
    public sealed class RecycleStorageFile : FileSystemStorageFile, IRecycleStorageItem
    {
        public string OriginPath { get; }

        public DateTimeOffset DeleteTime { get; }

        public override string Name => System.IO.Path.GetFileName(OriginPath);

        public override string DisplayName => Name;

        public override string DisplayType => ((StorageItem as StorageFile)?.DisplayType) ?? (string.IsNullOrEmpty(InnerDisplayType) ? Type : InnerDisplayType);

        public override string Type => ((StorageItem as StorageFile)?.FileType) ?? System.IO.Path.GetExtension(OriginPath).ToUpper();

        private string InnerDisplayType;

        protected override async Task LoadCoreAsync(bool ForceUpdate)
        {
            if (Regex.IsMatch(Name, @"\.(lnk|url)$", RegexOptions.IgnoreCase))
            {
                if (GetBulkAccessSharedController(out var ControllerRef))
                {
                    using (ControllerRef)
                    {
                        InnerDisplayType = await ControllerRef.Value.Controller.GetFriendlyTypeNameAsync(Type);
                    }
                }
                else
                {
                    using (FullTrustProcessController.Exclusive Exclusive = await FullTrustProcessController.GetAvailableControllerAsync(PriorityLevel.Low))
                    {
                        InnerDisplayType = await Exclusive.Controller.GetFriendlyTypeNameAsync(Type);
                    }
                }
            }

            await base.LoadCoreAsync(ForceUpdate);
        }

        protected override Task<StorageFile> GetStorageItemCoreAsync()
        {
            if (Regex.IsMatch(Name, @"\.(lnk|url)$", RegexOptions.IgnoreCase))
            {
                return Task.FromResult<StorageFile>(null);
            }
            else
            {
                return base.GetStorageItemCoreAsync();
            }
        }

        public override async Task DeleteAsync(bool PermanentDelete, CancellationToken CancelToken = default, ProgressChangedEventHandler ProgressHandler = null)
        {
            using (FullTrustProcessController.Exclusive Exclusive = await FullTrustProcessController.GetAvailableControllerAsync())
            {
                if (!await Exclusive.Controller.DeleteItemInRecycleBinAsync(Path))
                {
                    throw new Exception();
                }
            }
        }

        public RecycleStorageFile(NativeFileData Data, string OriginPath, DateTimeOffset DeleteTime) : base(Data)
        {
            this.OriginPath = OriginPath;
            this.DeleteTime = DeleteTime.ToLocalTime();
        }

        public async Task<bool> RestoreAsync()
        {
            using (FullTrustProcessController.Exclusive Exclusive = await FullTrustProcessController.GetAvailableControllerAsync())
            {
                return await Exclusive.Controller.RestoreItemInRecycleBinAsync(OriginPath);
            }
        }
    }
}
