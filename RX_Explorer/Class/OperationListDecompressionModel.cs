﻿using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RX_Explorer.Class
{
    public class OperationListDecompressionModel : OperationListBaseModel
    {
        public override string OperationKindText => Globalization.GetString("TaskList_OperationKind_Decompression");

        public override string FromDescription
        {
            get
            {
                if (DecompressionFrom.Length > 5)
                {
                    return $"{Globalization.GetString("TaskList_From_Label")}: {Environment.NewLine}{string.Join(Environment.NewLine, DecompressionFrom.Take(5))}{Environment.NewLine}({DecompressionFrom.Length - 5} {Globalization.GetString("TaskList_More_Items")})...";
                }
                else
                {
                    return $"{Globalization.GetString("TaskList_From_Label")}: {Environment.NewLine}{string.Join(Environment.NewLine, DecompressionFrom)}";
                }
            }
        }

        public override string ToDescription => $"{Globalization.GetString("TaskList_To_Label")}: {Environment.NewLine}{DecompressionTo}";

        public Encoding Encoding { get; }

        public bool ShouldCreateFolder { get; }

        public string[] DecompressionFrom { get; }

        public string DecompressionTo { get; }

        public override bool CanBeCancelled => true;

        protected override async Task<ProgressCalculator> PrepareSizeDataCoreAsync(CancellationToken Token)
        {
            ulong TotalSize = 0;

            await foreach (FileSystemStorageItemBase Item in FileSystemStorageItemBase.OpenInBatchAsync(DecompressionFrom, Token))
            {
                switch (Item)
                {
                    case FileSystemStorageFolder Folder:
                        {
                            TotalSize += await Folder.GetFolderSizeAsync(Token);
                            break;
                        }
                    case FileSystemStorageFile File:
                        {
                            TotalSize += File.Size;
                            break;
                        }
                }
            }

            return new ProgressCalculator(TotalSize);
        }

        public OperationListDecompressionModel(string[] DecompressionFrom, string DecompressionTo, bool ShouldCreateFolder, Encoding Encoding = null)
        {
            if (string.IsNullOrWhiteSpace(DecompressionTo))
            {
                throw new ArgumentNullException(nameof(DecompressionTo), "Parameter could not be empty or null");
            }

            if (DecompressionFrom.Any((Path) => string.IsNullOrWhiteSpace(Path)))
            {
                throw new ArgumentNullException(nameof(DecompressionFrom), "Parameter could not be empty or null");
            }

            this.DecompressionFrom = DecompressionFrom;
            this.DecompressionTo = DecompressionTo;
            this.Encoding = Encoding ?? Encoding.Default;
            this.ShouldCreateFolder = ShouldCreateFolder;
        }
    }
}
