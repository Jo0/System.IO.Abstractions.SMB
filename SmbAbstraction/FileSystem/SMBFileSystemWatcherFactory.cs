﻿using System;
using System.IO.Abstractions;

namespace SmbAbstraction
{
    public class SMBFileSystemWatcherFactory : FileSystemWatcherFactory, IFileSystemWatcherFactory
    {
        public new IFileSystemWatcher FromPath(string path)
        {
            if (path.IsSharePath())
            {
                return base.FromPath(path);
            }

            throw new NotSupportedException();
        }
    }
}
