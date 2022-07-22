﻿using SharedLibrary;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RX_Explorer.Class
{
    public class OperationListRemoteModel : OperationListCopyModel
    {
        public override string OperationKindText => Globalization.GetString("TaskList_OperationKind_RemoteCopy");

        public override string FromDescription => string.Empty;

        protected override async Task<ProgressCalculator> PrepareSizeDataCoreAsync(CancellationToken Token)
        {
            using (AuxiliaryTrustProcessController.Exclusive Exclusive = await AuxiliaryTrustProcessController.GetControllerExclusiveAsync())
            {
                RemoteClipboardRelatedData Data = await Exclusive.Controller.GetRemoteClipboardRelatedDataAsync();

                if ((Data?.TotalSize).GetValueOrDefault() > 0)
                {
                    return new ProgressCalculator(Data.TotalSize);
                }
            }

            return null;
        }

        public OperationListRemoteModel(string ToPath) : base(Array.Empty<string>(), ToPath)
        {

        }
    }
}
