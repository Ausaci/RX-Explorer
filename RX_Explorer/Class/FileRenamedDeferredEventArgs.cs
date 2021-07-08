﻿namespace RX_Explorer.Class
{
    class FileRenamedDeferredEventArgs : FileChangedDeferredEventArgs
    {
        public string NewName { get; }

        public FileRenamedDeferredEventArgs(string Path, string NewName) : base(Path)
        {
            this.NewName = NewName;
        }
    }
}
