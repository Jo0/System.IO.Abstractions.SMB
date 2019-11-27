﻿using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using SmbLibraryStd;
using SmbLibraryStd.Client;

namespace System.IO.Abstractions.SMB
{
    public class SMBDirectory : DirectoryWrapper, IDirectory
    {
        private readonly ISMBClientFactory _smbClientFactory;
        private readonly IFileSystem _fileSystem;
        private readonly ISMBCredentialProvider _credentialProvider;
        private SMBDirectoryInfoFactory _directoryInfoFactory => _fileSystem.DirectoryInfo as SMBDirectoryInfoFactory;

        public SMBTransportType transport { get; set; }

        public SMBDirectory(ISMBClientFactory smbclientFactory, ISMBCredentialProvider credentialProvider, IFileSystem fileSystem) : base(new FileSystem())
        {
            _smbClientFactory = smbclientFactory;
            _credentialProvider = credentialProvider;
            _fileSystem = fileSystem;
            transport = SMBTransportType.DirectTCPTransport;
        }

        public override IDirectoryInfo CreateDirectory(string path)
        {
            return CreateDirectory(path, null);
        }

        private IDirectoryInfo CreateDirectory(string path, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                return base.CreateDirectory(path);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            AccessMask accessMask = AccessMask.MAXIMUM_ALLOWED;
            ShareAccess shareAccess = ShareAccess.None;
            CreateDisposition disposition = CreateDisposition.FILE_CREATE;
            CreateOptions createOptions = CreateOptions.FILE_DIRECTORY_FILE;

            if (credential == null)
            {
                credential = _credentialProvider.GetSMBCredential(path);
            }

            if (credential == null)
            {
                throw new Exception($"Unable to find credential for path: {path}");
            }

            using var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential);

            var shareName = path.ShareName();
            var relativePath = path.RelativeSharePath();

            ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

            status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, relativePath, accessMask, 0, shareAccess,
                disposition, createOptions, null);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                throw new IOException($"Unable to connect to smbShare. Status = {status}, FileStatus = {fileStatus}");
            }
            fileStore.CloseFile(handle);

            return _directoryInfoFactory.FromDirectoryName(path, credential);
        }

        public override void Delete(string path)
        {
            Delete(path, null);
        }

        internal void Delete(string path, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                base.Delete(path);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            if(credential == null)
            {
                credential = _credentialProvider.GetSMBCredential(path);
            }

            if(EnumerateFileSystemEntries(path).Count() > 0)
            {
                throw new IOException("Cannot delete directory. Directory is not empty.");
            }

            using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
            {
                var shareName = path.ShareName();
                var relativePath = path.RelativeSharePath();

                ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, relativePath, AccessMask.DELETE, 0, ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DELETE_ON_CLOSE, null);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Unable to connect to smbShare. Status = {status}");
                }

                // This is the correct delete command, but it doesn't work for some reason. You have to use FILE_DELETE_ON_CLOSE
                // fileStore.SetFileInformation(handle, new FileDispositionInformation());
                
                fileStore.CloseFile(handle);
            }
        }

        public override void Delete(string path, bool recursive)
        {
            Delete(path, recursive, null);
        }

        public void Delete(string path, bool recursive, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                base.Delete(path, recursive);
            }

            if (recursive)
            {
                var hostEntry = Dns.GetHostEntry(path.HostName());
                var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

                NTStatus status = NTStatus.STATUS_SUCCESS;

                if (credential == null)
                {
                    credential = _credentialProvider.GetSMBCredential(path);
                }

                using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
                {
                    var shareName = path.ShareName();
                    var relativePath = path.RelativeSharePath();

                    ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                    int attempts = 0;
                    int allowedRetrys = 3;
                    object handle;

                    do
                    {
                        attempts++;

                        status = fileStore.CreateFile(out handle, out FileStatus fileStatus, relativePath, AccessMask.GENERIC_READ, 0, ShareAccess.Delete,
                            CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                    }
                    while (status == NTStatus.STATUS_PENDING && attempts < allowedRetrys);

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new IOException($"Unable to connect to smbShare. Status = {status}");
                    }

                    fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> queryDirectoryFileInformation, handle, "*", FileInformationClass.FileDirectoryInformation);

                    foreach (var file in queryDirectoryFileInformation)
                    {
                        if (file.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                        {
                            FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;
                            if (fileDirectoryInformation.FileName == "."
                                || fileDirectoryInformation.FileName == ".."
                                || fileDirectoryInformation.FileName == ".DS_Store")
                            {
                                continue;
                            }
                            else if (fileDirectoryInformation.FileAttributes.HasFlag(SmbLibraryStd.FileAttributes.Directory))
                            {
                                Delete(Path.Combine(path, fileDirectoryInformation.FileName), recursive, credential);
                            }

                            _fileSystem.File.Delete(Path.Combine(path, fileDirectoryInformation.FileName));
                        }
                    }
                    fileStore.CloseFile(handle);

                    Delete(path, credential);
                }
            }
            else
            {
                Delete(path);
            }
        }

        public override IEnumerable<string> EnumerateDirectories(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateDirectories(path);
            }

            return EnumerateDirectories(path, "*");
        }

        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateDirectories(path, searchPattern);
            }

            return EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateDirectories(path, searchPattern, searchOption, null);
        }

        private IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateDirectories(path, searchPattern, searchOption);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            if (credential == null)
            {
                credential = _credentialProvider.GetSMBCredential(path);
            }

            using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
            {
                var shareName = path.ShareName();
                var relativePath = path.RelativeSharePath();

                ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, relativePath, AccessMask.GENERIC_READ, 0, ShareAccess.Read,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Unable to connect to smbShare. Status = {status}");
                }

                fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> queryDirectoryFileInformation, handle, searchPattern, FileInformationClass.FileDirectoryInformation);


                List<string> files = new List<string>();

                foreach (var file in queryDirectoryFileInformation)
                {
                    if (file.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                    {
                        FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;

                        if (fileDirectoryInformation.FileName == "." || fileDirectoryInformation.FileName == "..")
                        {
                            continue;
                        }

                        if (fileDirectoryInformation.FileAttributes.HasFlag(SmbLibraryStd.FileAttributes.Directory))
                        {
                            files.Add(Path.Combine(path, fileDirectoryInformation.FileName));
                            if (searchOption == SearchOption.AllDirectories)
                            {
                                files.AddRange(EnumerateDirectories(Path.Combine(path, fileDirectoryInformation.FileName), searchPattern, searchOption, credential));
                            }
                        }
                    }
                }
                fileStore.CloseFile(handle);

                return files;
            }
        }

        public override IEnumerable<string> EnumerateFiles(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFiles(path);
            }

            return EnumerateFiles(path, "*");
        }

        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFiles(path, searchPattern);
            }

            return EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFiles(path, searchPattern, searchOption, null);
        }

        private IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFiles(path, searchPattern, searchOption);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            if (credential == null)
            {
                credential = _credentialProvider.GetSMBCredential(path);
            }

            using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
            {
                var shareName = path.ShareName();
                var relativePath = path.RelativeSharePath();

                ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, relativePath, AccessMask.GENERIC_READ, 0, ShareAccess.Read,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Unable to connect to smbShare. Status = {status}");
                }

                fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> queryDirectoryFileInformation, handle, searchPattern, FileInformationClass.FileDirectoryInformation);


                List<string> files = new List<string>();

                foreach (var file in queryDirectoryFileInformation)
                {
                    if (file.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                    {
                        FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;
                        if (fileDirectoryInformation.FileName == "."
                            || fileDirectoryInformation.FileName == ".."
                            || fileDirectoryInformation.FileName == ".DS_Store")
                        {
                            continue;
                        }

                        if (fileDirectoryInformation.FileAttributes.HasFlag(SmbLibraryStd.FileAttributes.Directory))
                        {
                            if (searchOption == SearchOption.AllDirectories)
                            {
                                files.AddRange(EnumerateFiles(Path.Combine(path, fileDirectoryInformation.FileName), searchPattern, searchOption, credential));
                            }
                        }
                        else
                        {
                            files.Add(Path.Combine(path, fileDirectoryInformation.FileName));
                        }
                    }
                }
                fileStore.CloseFile(handle);

                return files;
            }
        }

        public override IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFileSystemEntries(path);
            }

            return EnumerateFileSystemEntries(path, "*");
        }

        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFileSystemEntries(path, searchPattern);
            }

            return EnumerateFileSystemEntries(path, searchPattern, SearchOption.TopDirectoryOnly);
        }


        public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileSystemEntries(path, searchPattern, searchOption, null);
        }

        private IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                return base.EnumerateFileSystemEntries(path, searchPattern, searchOption);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            if (credential == null)
            {
                credential = _credentialProvider.GetSMBCredential(path);
            }

            using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
            {
                var shareName = path.ShareName();
                var relativePath = path.RelativeSharePath();

                ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, relativePath, AccessMask.GENERIC_READ, 0, ShareAccess.Read,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Unable to connect to smbShare. Status = {status}");
                }

                fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> queryDirectoryFileInformation, handle, searchPattern, FileInformationClass.FileDirectoryInformation);


                List<string> files = new List<string>();

                foreach (var file in queryDirectoryFileInformation)
                {
                    if (file.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                    {
                        FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;
                        if (fileDirectoryInformation.FileName == "." || fileDirectoryInformation.FileName == ".." || fileDirectoryInformation.FileName == ".DS_Store")
                        {
                            continue;
                        }


                        if (fileDirectoryInformation.FileAttributes.HasFlag(SmbLibraryStd.FileAttributes.Directory))
                        {
                            if (searchOption == SearchOption.AllDirectories)
                            {
                                files.AddRange(EnumerateFileSystemEntries(Path.Combine(path, fileDirectoryInformation.FileName), searchPattern, searchOption, credential));
                            }
                        }

                        files.Add(Path.Combine(path, fileDirectoryInformation.FileName));
                    }
                }
                fileStore.CloseFile(handle);

                return files;
            }
        }

        public override bool Exists(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.Exists(path);
            }

            var hostEntry = Dns.GetHostEntry(path.HostName());
            var ipAddress = hostEntry.AddressList.First(a => a.AddressFamily == Net.Sockets.AddressFamily.InterNetwork);

            NTStatus status = NTStatus.STATUS_SUCCESS;

            var credential = _credentialProvider.GetSMBCredential(path);

            using (var connection = SMBConnection.CreateSMBConnection(_smbClientFactory, ipAddress, transport, credential))
            {
                var shareName = path.ShareName();
                var relativePath = path.RelativeSharePath();
                var directoryPath = Path.GetDirectoryName(relativePath);

                ISMBFileStore fileStore = connection.SMBClient.TreeConnect(shareName, out status);

                status = fileStore.CreateFile(out object handle, out FileStatus fileStatus, directoryPath, AccessMask.GENERIC_READ, 0, ShareAccess.Read,
                    CreateDisposition.FILE_OPEN, CreateOptions.FILE_DIRECTORY_FILE, null);
                if (status != NTStatus.STATUS_SUCCESS)
                {
                    throw new IOException($"Unable to connect to smbShare. Status = {status}");
                }

                fileStore.QueryDirectory(out List<QueryDirectoryFileInformation> queryDirectoryFileInformation, handle, string.IsNullOrEmpty(directoryPath) ? "*" : directoryPath, FileInformationClass.FileDirectoryInformation);

                foreach (var file in queryDirectoryFileInformation)
                {
                    if (file.FileInformationClass == FileInformationClass.FileDirectoryInformation)
                    {
                        FileDirectoryInformation fileDirectoryInformation = (FileDirectoryInformation)file;
                        if (fileDirectoryInformation.FileName == Path.GetFileName(relativePath))
                        {
                            fileStore.CloseFile(handle);
                            return true;
                        }
                    }
                }

                fileStore.CloseFile(handle);
            }

            return false;
        }

        public override DirectorySecurity GetAccessControl(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetAccessControl(path);
            }

            throw new NotSupportedException();
        }

        public override DirectorySecurity GetAccessControl(string path, AccessControlSections includeSections)
        {
            if (!path.IsSmbPath())
            {
                return base.GetAccessControl(path, includeSections);
            }

            throw new NotSupportedException();
        }

        public override DateTime GetCreationTime(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetCreationTime(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).CreationTime;
        }

        public override DateTime GetCreationTimeUtc(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetCreationTimeUtc(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).CreationTimeUtc;
        }

        public override string GetCurrentDirectory()
        {
            throw new NotImplementedException();
        }

        public override string[] GetDirectories(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetDirectories(path);
            }

            return GetDirectories(path, "*");
        }

        public override string[] GetDirectories(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.GetDirectories(path, searchPattern);
            }

            return GetDirectories(path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public override string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            if (!path.IsSmbPath())
            {
                return base.GetDirectories(path, searchPattern, searchOption);
            }

            return EnumerateDirectories(path, searchPattern, searchOption).ToArray();
        }

        public override string GetDirectoryRoot(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetDirectoryRoot(path);
            }

            return Path.GetPathRoot(path);
        }

        public override string[] GetFiles(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetFiles(path);
            }

            return GetFiles(path, "*");
        }

        public override string[] GetFiles(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.GetFiles(path, searchPattern);
            }

            return GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
        }

        public override string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        {
            if (!path.IsSmbPath())
            {
                return base.GetFiles(path, searchPattern, searchOption);
            }

            return EnumerateFiles(path, searchPattern, searchOption).ToArray();
        }

        public override string[] GetFileSystemEntries(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetFileSystemEntries(path);
            }

            return GetFileSystemEntries(path, "*");
        }

        public override string[] GetFileSystemEntries(string path, string searchPattern)
        {
            if (!path.IsSmbPath())
            {
                return base.GetFileSystemEntries(path, searchPattern);
            }

            return EnumerateFileSystemEntries(path, searchPattern).ToArray();
        }

        public override DateTime GetLastAccessTime(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetLastAccessTime(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).LastAccessTime;
        }

        public override DateTime GetLastAccessTimeUtc(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetLastAccessTimeUtc(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).LastAccessTimeUtc;
        }

        public override DateTime GetLastWriteTime(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetLastWriteTime(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).LastWriteTime;
        }

        public override DateTime GetLastWriteTimeUtc(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetLastWriteTimeUtc(path);
            }

            return _directoryInfoFactory.FromDirectoryName(path).LastWriteTimeUtc;
        }

        public override IDirectoryInfo GetParent(string path)
        {
            if (!path.IsSmbPath())
            {
                return base.GetParent(path);
            }

            return GetParent(path, null);
        }

        internal IDirectoryInfo GetParent(string path, ISMBCredential credential)
        {
            if (!path.IsSmbPath())
            {
                return base.GetParent(path);
            }

            var pathUri = new Uri(path);
            var parentUri = pathUri.AbsoluteUri.EndsWith('/') ? new Uri(pathUri, "..") : new Uri(pathUri, ".");

            return _directoryInfoFactory.FromDirectoryName(parentUri.AbsoluteUri, credential);
        }

        public override void Move(string sourceDirName, string destDirName)
        {
            Move(sourceDirName, destDirName, null, null);
        }

        private void Move(string sourceDirName, string destDirName, ISMBCredential sourceCredential, ISMBCredential destinationCredential)
        {
            if(sourceCredential == null)
            {
                sourceCredential = _credentialProvider.GetSMBCredential(sourceDirName);
            }

            if(destinationCredential == null)
            {
                destinationCredential = _credentialProvider.GetSMBCredential(destDirName);
            }

            CreateDirectory(destDirName, destinationCredential);

            var dirs = EnumerateDirectories(sourceDirName, "*", SearchOption.TopDirectoryOnly, sourceCredential);

            foreach (var dir in dirs)
            {
                var destDirPath = Path.Combine(destDirName, new Uri(dir).Segments.Last());
                Move(dir, destDirPath, sourceCredential, destinationCredential);
            }

            var files = EnumerateFiles(sourceDirName, "*", SearchOption.TopDirectoryOnly, sourceCredential);

            foreach(var file in files)
            {
                var destFilePath = Path.Combine(destDirName, new Uri(file).Segments.Last());
                SMBFile smbFile = _fileSystem.File as SMBFile;
                smbFile.Move(file, destFilePath, sourceCredential, destinationCredential);
            }
        }

        public override void SetAccessControl(string path, DirectorySecurity directorySecurity)
        {
            if (!path.IsSmbPath())
            {
                base.SetAccessControl(path, directorySecurity);
            }

            throw new NotSupportedException();
        }

        public override void SetCreationTime(string path, DateTime creationTime)
        {
            if (!path.IsSmbPath())
            {
                base.SetCreationTime(path, creationTime);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.CreationTime = creationTime.ToUniversalTime();
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }

        public override void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
        {
            if (!path.IsSmbPath())
            {
                base.SetCreationTimeUtc(path, creationTimeUtc);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.CreationTime = creationTimeUtc;
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }

        public override void SetCurrentDirectory(string path)
        {
            if (!path.IsSmbPath())
            {
                base.SetCurrentDirectory(path);
            }

            throw new NotImplementedException();
        }

        public override void SetLastAccessTime(string path, DateTime lastAccessTime)
        {
            if (!path.IsSmbPath())
            {
                base.SetLastAccessTime(path, lastAccessTime);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.LastAccessTime = lastAccessTime.ToUniversalTime();
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }

        public override void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
        {
            if (!path.IsSmbPath())
            {
                base.SetLastAccessTimeUtc(path, lastAccessTimeUtc);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.LastAccessTime = lastAccessTimeUtc;
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }

        public override void SetLastWriteTime(string path, DateTime lastWriteTime)
        {
            if (!path.IsSmbPath())
            {
                base.SetLastWriteTime(path, lastWriteTime);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.LastWriteTime = lastWriteTime.ToUniversalTime();
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }

        public override void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            if (!path.IsSmbPath())
            {
                base.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
            }

            var dirInfo = _directoryInfoFactory.FromDirectoryName(path);
            dirInfo.LastWriteTime = lastWriteTimeUtc;
            _directoryInfoFactory.SaveDirectoryInfo((SMBDirectoryInfo)dirInfo);
        }
    }
}