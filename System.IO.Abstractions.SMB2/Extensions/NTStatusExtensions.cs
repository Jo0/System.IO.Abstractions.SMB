﻿using SmbLibraryStd;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Text;

namespace System.IO.Abstractions.SMB
{
    public static class NTStatusExtensions
    {
        //https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-smb/6ab6ca20-b404-41fd-b91a-2ed39e3762ea
        //https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-cifs/8f11e0f3-d545-46cc-97e6-f00569e3e1bc
        //Status codes and messages
        //Format:
        // "{Error code} - {POSIX code (if applicable) - {Description}"

        //ERRDOS Class 
        private const string ERRBadFunc = "ERRbadfunc(0x0001) - EINVAL - Invalid Function";
        private const string ERRBadFile = "ERRbadFile(0x0002) - EOENT - File Not Found";
        private const string ERRBadPath = "ERRbadpath(0x0003) - ENOENT - A component in the path prefix is not a directory";
        private const string ERRNoFids = "ERRnofids(0x0004) - EMFILE - Too many open files. No FIDs are available";
        private const string ERRNoAccess = "ERRnoaccess(0x0005) - EPERM - Access denied";
        private const string ERRBadFid = "ERRbadfid(0x0006) - EBADF - Invalid FID";
        private const string ERRNoMem = "ERRnomem(0x0008) - ENOMEM - Insufficient server memory to perform the requested operation";
        private const string ERRBadAccess = "ERRbadaccess(0x000C) - Invalid open mode";
        private const string ERRBadData = "ERRbaddata(0x000D) - E2BIG - Bad data (May be generated by IOCTL calls on the server.)";
        private const string ERRRemCd = "ERRremcd(0x0010) - Remove of directory failed because it was not empty";
        private const string ERRNoFiles = "ERRnofiles(0x0012) - No (more) files found following a file search command";
        private const string ERREof = "ERReof(0x0026) - EEOF - Attempted to read beyond the end of the file";
        private const string ERRUnsup = "ERRunsup(0x0032) - This command is not supported by the server";
        private const string ERRFileExists = "ERRfilexists(0x0050) - EEXIST - An attempt to create a file or directory failed because an object with the same pathname already exists";
        private const string ERRInvalidParam = "ERRinvalidparam(0x0057) - A parameter supplied with the message is invalid";
        private const string ERRUnknownLevel = "ERRunknownlevel(0x007C) - Invalid information level";
        private const string ERROR_NOT_LOCKED = "ERROR_NOT_LOCKED(0x009E) - The byte range specified in an unlock request was not locked";
        private const string ERROR_NO_MORE_SEARCH_HANDLES = "ERROR_NO_MORE_SEARCH_HANDLES(0x0071) - Maximum number of searches has been exhausted.";
        private const string ERRBadPipe = "ERRbadpipe(0x00E6) - Invalid named pipe";
        private const string ERRMoreData = "ERRmoredata(0x00EA) - There is more data available to read on the designated named pipe";
        private const string ERR_NOTIFY_ENUM_DIR = "ERR_NOTIFY_ENUM_DIR(0x03FE) - More changes have occurred within the directory than will fit within the specified Change Notify response buffer";

        //ERRSRV  Class


        public static void HandleStatus(this NTStatus status)
        {
            switch (status)
            {
                //ERRDOS Class
                case (NTStatus.STATUS_NOT_IMPLEMENTED):
                    throw new NotImplementedException($"{status.ToString()}: {ERRBadFunc}");
                case (NTStatus.STATUS_INVALID_DEVICE_REQUEST):
                    throw new InvalidOperationException($"{status.ToString()}: {ERRBadFunc}");
                case (NTStatus.STATUS_NO_SUCH_FILE):
                case (NTStatus.STATUS_NO_SUCH_DEVICE):
                case (NTStatus.STATUS_OBJECT_NAME_NOT_FOUND):
                    throw new FileNotFoundException($"{status.ToString()}: {ERRBadFile}");
                case (NTStatus.STATUS_OBJECT_PATH_INVALID):
                case (NTStatus.STATUS_OBJECT_PATH_NOT_FOUND):
                case (NTStatus.STATUS_OBJECT_PATH_SYNTAX_BAD):
                    throw new DirectoryNotFoundException($"{status.ToString()}: {ERRBadPath}");
                case (NTStatus.STATUS_TOO_MANY_OPENED_FILES):
                    throw new FileNotFoundException($"{status.ToString()}: {ERRNoFids}");
                case (NTStatus.STATUS_ACCESS_DENIED):
                case (NTStatus.STATUS_DELETE_PENDING):
                case (NTStatus.STATUS_PRIVILEGE_NOT_HELD):
                case (NTStatus.STATUS_LOGON_FAILURE):
                case (NTStatus.STATUS_FILE_IS_A_DIRECTORY):
                case (NTStatus.STATUS_CANNOT_DELETE):
                    throw new UnauthorizedAccessException($"{status.ToString()}: {ERRNoAccess}");
                case (NTStatus.STATUS_SMB_BAD_FID):
                case (NTStatus.STATUS_INVALID_HANDLE):
                case (NTStatus.STATUS_FILE_CLOSED):
                    throw new ArgumentException($"{status.ToString()}: {ERRBadFid}");
                case (NTStatus.STATUS_INSUFF_SERVER_RESOURCES):
                    throw new OutOfMemoryException($"{status.ToString()}:{ERRNoMem}");
                case (NTStatus.STATUS_OS2_INVALID_ACCESS):
                    throw new UnauthorizedAccessException($"{status.ToString()}: {ERRBadAccess}");
                case (NTStatus.STATUS_DATA_ERROR):
                    throw new InvalidDataException($"{status.ToString()}: {ERRBadData}");
                case (NTStatus.STATUS_DIRECTORY_NOT_EMPTY):
                    throw new IOException($"{status.ToString()}: {ERRRemCd}");
                case (NTStatus.STATUS_NO_MORE_FILES):
                    throw new IOException($"{status.ToString()}: {ERRNoFiles}");
                case (NTStatus.STATUS_NOT_SUPPORTED):
                    throw new NotSupportedException($"{status.ToString()}: {ERRUnsup}");
                case (NTStatus.STATUS_OBJECT_NAME_COLLISION):
                    throw new IOException($"{status.ToString()}: {ERRFileExists}");
                case (NTStatus.STATUS_INVALID_PARAMETER):
                    throw new ArgumentException($"{status.ToString()}: {ERRInvalidParam}");
                case (NTStatus.STATUS_OS2_INVALID_LEVEL):
                    throw new ArgumentException($"{status.ToString()}: {ERRUnknownLevel}");
                case (NTStatus.STATUS_RANGE_NOT_LOCKED):
                    throw new AccessViolationException($"{status.ToString()}: {ERROR_NOT_LOCKED}");
                case (NTStatus.STATUS_OS2_NO_MORE_SIDS):
                    throw new InvalidOperationException($"{status.ToString()}: {ERROR_NO_MORE_SEARCH_HANDLES}");
                case (NTStatus.STATUS_INVALID_INFO_CLASS):
                    throw new ArgumentException($"{status.ToString()}: {ERRBadPipe}");
                case (NTStatus.STATUS_BUFFER_OVERFLOW):
                case (NTStatus.STATUS_MORE_PROCESSING_REQUIRED):
                    throw new InternalBufferOverflowException($"{status.ToString()}: {ERRMoreData}");
                case (NTStatus.STATUS_NOTIFY_ENUM_DIR):
                    throw new AccessViolationException($"{status.ToString()}: {ERR_NOTIFY_ENUM_DIR}");

                //ERRSRV Class
                case (NTStatus.STATUS_INVALID_SMB):
                    throw new ArgumentException("Invalid Handle.");
                case (NTStatus.STATUS_INVALID_INFO_CLASS):
                    throw new ArgumentException("Invalid Information Class.");
                case (NTStatus.STATUS_INVALID_PARAMETER):
                    throw new ArgumentException("Invalid Parameter.");
                case (NTStatus.STATUS_NO_SUCH_FILE):
                    throw new FileNotFoundException();
                case (NTStatus.STATUS_CANNOT_DELETE):
                    throw new IOException("Cannot delete.");
                case (NTStatus.STATUS_DIRECTORY_NOT_EMPTY):
                    throw new IOException("The directory trying to be deleted is not empty.");
                case (NTStatus.STATUS_INVALID_SMB):
                //case (NTStatus.STATUS_INVALID_DEVICE_REQUEST):
                case (NTStatus.STATUS_NO_SUCH_DEVICE):
                    throw new DriveNotFoundException();
                case (NTStatus.STATUS_BAD_NETWORK_NAME):
                    throw new Exception("The network name cannot be found.");
                case (NTStatus.STATUS_NETWORK_NAME_DELETED):
                    throw new DriveNotFoundException("Network name has been deleted");
                case (NTStatus.STATUS_FILE_IS_A_DIRECTORY):
                    throw new IOException("The file is a directory.");
                case (NTStatus.STATUS_END_OF_FILE):
                    throw new IOException("End of file");
                case (NTStatus.STATUS_DISK_FULL):
                    throw new IOException("Disk is full.");
                case (NTStatus.STATUS_ACCESS_DENIED):
                case (NTStatus.STATUS_INVALID_LOGON_HOURS):
                case (NTStatus.STATUS_INVALID_WORKSTATION):
                case (NTStatus.STATUS_LOGON_TYPE_NOT_GRANTED):
                case (NTStatus.STATUS_OS2_INVALID_ACCESS):
                    throw new UnauthorizedAccessException();
                case (NTStatus.STATUS_ACCOUNT_EXPIRED):
                case (NTStatus.STATUS_ACCOUNT_DISABLED):
                case (NTStatus.STATUS_ACCOUNT_LOCKED_OUT):
                case (NTStatus.STATUS_ACCOUNT_RESTRICTION):
                case (NTStatus.STATUS_LOGON_FAILURE):
                case (NTStatus.STATUS_PASSWORD_MUST_CHANGE):
                case (NTStatus.STATUS_PASSWORD_EXPIRED):
                case (NTStatus.SEC_E_INVALID_TOKEN):
                    throw new AuthenticationException();
                case (NTStatus.STATUS_BUFFER_OVERFLOW):
                    throw new InternalBufferOverflowException();
                case (NTStatus.STATUS_SUCCESS):
                case (NTStatus.STATUS_PENDING):
                case (NTStatus.STATUS_DELETE_PENDING):
                case (NTStatus.STATUS_CANCELLED):
                    break;
                default:
                    break;
            }
        }
    }
}