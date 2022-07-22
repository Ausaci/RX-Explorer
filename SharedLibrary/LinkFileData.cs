﻿using System;

namespace SharedLibrary
{
    public sealed class LinkFileData
    {
        public string LinkPath { get; set; }

        public string LinkTargetPath { get; set; }

        public string[] Arguments { get; set; }

        public string Comment { get; set; }

        public string WorkDirectory { get; set; }
        
        public WindowState WindowState { get; set; }

        public byte HotKey { get; set; }

        public bool NeedRunAsAdmin { get; set; }

        public byte[] IconData { get; set; } 
    }
}
