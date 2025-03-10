﻿using MediaDevices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using MimeTypes;
using SharedLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace AuxiliaryTrustProcess.Class
{
    internal static class Helper
    {
        public static bool CheckIfPathIsNetworkPath(string Path)
        {
            if (!ShlwApi.PathIsUNC(Path))
            {
                return ShlwApi.PathIsNetworkPath(Path);
            }

            return true;
        }

        public static string MapUncPathToDrivePath(string UncPath)
        {
            if (ShlwApi.PathIsUNC(UncPath))
            {
                try
                {
                    NetApi32.SHARE_INFO_502 ShareInfo = NetApi32.NetShareGetInfo<NetApi32.SHARE_INFO_502>(null, UncPath, 502);

                    if (!ShareInfo.shi502_security_descriptor.IsValidSecurityDescriptor())
                    {
                        LogTracer.Log($"{UncPath} is do not have an valid \"Security Descripto\" when convert to drive path");
                    }

                    if (!string.IsNullOrEmpty(ShareInfo.shi502_path))
                    {
                        return ShareInfo.shi502_path;
                    }
                }
                catch (Exception)
                {
                    //No need to handle this exception
                }
            }

            return string.Empty;
        }

        public static bool IsTopMostWindow(HWND WindowHandle)
        {
            return ((User32.WindowStylesEx)User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_EXSTYLE)).HasFlag(User32.WindowStylesEx.WS_EX_TOPMOST);
        }

        public static string GetActualNamedPipeFromUwpApplication(string PipeId, string AppContainerName = null, int ProcessId = 0)
        {
            if (ProcessId > 0)
            {
                using (Process TargetProcess = Process.GetProcessById(ProcessId))
                using (AdvApi32.SafeHTOKEN Token = AdvApi32.SafeHTOKEN.FromProcess(TargetProcess, AdvApi32.TokenAccess.TOKEN_ALL_ACCESS))
                {
                    if (!Token.IsInvalid)
                    {
                        return $@"Sessions\{TargetProcess.SessionId}\AppContainerNamedObjects\{string.Join("-", Token.GetInfo<AdvApi32.TOKEN_APPCONTAINER_INFORMATION>(AdvApi32.TOKEN_INFORMATION_CLASS.TokenAppContainerSid).TokenAppContainer.ToString("D").Split(new string[] { "-" }, StringSplitOptions.RemoveEmptyEntries).Take(11))}\{PipeId}";
                    }
                }
            }

            if (!string.IsNullOrEmpty(AppContainerName))
            {
                using (Process CurrentProcess = Process.GetCurrentProcess())
                {
                    if (UserEnv.DeriveAppContainerSidFromAppContainerName(AppContainerName, out AdvApi32.SafeAllocatedSID Sid).Succeeded)
                    {
                        using (Sid)
                        {
                            return $@"Sessions\{CurrentProcess.SessionId}\AppContainerNamedObjects\{string.Join("-", ((PSID)Sid).ToString("D").Split(["-"], StringSplitOptions.RemoveEmptyEntries).Take(11))}\{PipeId}";
                        }
                    }
                }
            }

            return string.Empty;
        }

        public static IReadOnlyList<Process> GetLockingProcessesList(string Path)
        {
            StringBuilder SessionKey = new StringBuilder(Guid.NewGuid().ToString());

            if (RstrtMgr.RmStartSession(out uint SessionHandle, 0, SessionKey).Succeeded)
            {
                try
                {
                    string[] ResourcesFileName = new string[] { Path };

                    if (RstrtMgr.RmRegisterResources(SessionHandle, (uint)ResourcesFileName.Length, ResourcesFileName, 0, null, 0, null).Succeeded)
                    {
                        uint pnProcInfo = 0;

                        //Note: there's a race condition here -- the first call to RmGetList() returns
                        //      the total number of process. However, when we call RmGetList() again to get
                        //      the actual processes this number may have increased.
                        Win32Error Error = RstrtMgr.RmGetList(SessionHandle, out uint pnProcInfoNeeded, ref pnProcInfo, null, out _);

                        if (Error == Win32Error.ERROR_MORE_DATA)
                        {
                            RstrtMgr.RM_PROCESS_INFO[] ProcessInfo = new RstrtMgr.RM_PROCESS_INFO[pnProcInfoNeeded];

                            if (RstrtMgr.RmGetList(SessionHandle, out _, ref pnProcInfoNeeded, ProcessInfo, out _).Succeeded)
                            {
                                List<Process> LockProcesses = new List<Process>((int)pnProcInfoNeeded);

                                for (int i = 0; i < pnProcInfoNeeded; i++)
                                {
                                    try
                                    {
                                        LockProcesses.Add(Process.GetProcessById(Convert.ToInt32(ProcessInfo[i].Process.dwProcessId)));
                                    }
                                    catch (Exception ex)
                                    {
                                        LogTracer.Log(ex, "Process is no longer running");
                                    }
                                }

                                return LockProcesses;
                            }
                            else
                            {
                                LogTracer.Log("Could not list processes locking resource");
                            }
                        }
                        else if (Error != Win32Error.ERROR_SUCCESS)
                        {
                            LogTracer.Log("Could not list processes locking resource. Failed to get size of result.");
                        }
                    }
                    else
                    {
                        LogTracer.Log("Could not register resource");
                    }
                }
                finally
                {
                    RstrtMgr.RmEndSession(SessionHandle);
                }
            }
            else
            {
                LogTracer.Log("Could not begin restart session. Unable to determine locking process");
            }

            return new List<Process>(0);
        }

        public static ulong GetAllocationSize(string Path)
        {
            try
            {
                using (SafeFileHandle Handle = File.OpenHandle(Path))
                {
                    try
                    {
                        return Convert.ToUInt64(Math.Max(Kernel32.GetFileInformationByHandleEx<Kernel32.FILE_COMPRESSION_INFO>(Handle, Kernel32.FILE_INFO_BY_HANDLE_CLASS.FileCompressionInfo).CompressedFileSize, 0));
                    }
                    catch (Exception)
                    {
                        return Convert.ToUInt64(Math.Max(Kernel32.GetFileInformationByHandleEx<Kernel32.FILE_STANDARD_INFO>(Handle, Kernel32.FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo).AllocationSize, 0));
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the size from GetFileInformationByHandleEx so we could not calculate the size on disk");
            }

            return 0;
        }

        public static void CopyFileOrFolderTo(string From, string To)
        {
            static void CopyFileCore(string From, string To)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(To));

                try
                {
                    Kernel32.COPYFILE2_EXTENDED_PARAMETERS Parameters = new Kernel32.COPYFILE2_EXTENDED_PARAMETERS
                    {
                        dwSize = Convert.ToUInt32(Marshal.SizeOf<Kernel32.COPYFILE2_EXTENDED_PARAMETERS>()),
                        pProgressRoutine = (ref Kernel32.COPYFILE2_MESSAGE message, IntPtr _) =>
                        {
                            if (message.Type == Kernel32.COPYFILE2_MESSAGE_TYPE.COPYFILE2_CALLBACK_STREAM_FINISHED)
                            {
                                Kernel32.FlushFileBuffers(message.Info.StreamFinished.hDestinationFile);
                            }

                            return Kernel32.COPYFILE2_MESSAGE_ACTION.COPYFILE2_PROGRESS_CONTINUE;
                        }
                    };

                    if (File.GetAttributes(From).HasFlag(FileAttributes.Encrypted))
                    {
                        Parameters.dwCopyFlags = Kernel32.COPY_FILE.COPY_FILE_ALLOW_DECRYPTED_DESTINATION;
                    }

                    Kernel32.CopyFile2(From, To, ref Parameters).ThrowIfFailed();
                }
                catch (Exception)
                {
                    using (FileStream OriginStream = new FileStream(From, FileMode.Open, FileAccess.Read))
                    using (FileStream TargetStream = new FileStream(To, FileMode.Create, FileAccess.Write))
                    {
                        OriginStream.CopyTo(TargetStream);
                        TargetStream.Flush();
                    }
                }
            }

            if (File.Exists(From))
            {
                CopyFileCore(From, To);
            }
            else if (Directory.Exists(From))
            {
                if (Directory.Exists(To))
                {
                    Directory.Delete(To, true);
                }

                Directory.CreateDirectory(To);

                foreach (string SubPath in Directory.EnumerateFileSystemEntries(From, "*", SearchOption.AllDirectories))
                {
                    if (Directory.Exists(SubPath))
                    {
                        Directory.CreateDirectory(Path.Combine(To, Path.GetRelativePath(From, SubPath)));
                    }
                    else
                    {
                        CopyFileCore(SubPath, Path.Combine(To, Path.GetRelativePath(From, Path.GetDirectoryName(SubPath)), Path.GetFileName(SubPath)));
                    }
                }
            }
            else
            {
                throw new FileNotFoundException(From);
            }
        }

        public static byte[] GetThumbnailOverlay(string Path)
        {
            Shell32.SHFILEINFO Shfi = new Shell32.SHFILEINFO();

            IntPtr Result = Shell32.SHGetFileInfo(Path, 0, ref Shfi, Shell32.SHFILEINFO.Size, Shell32.SHGFI.SHGFI_OVERLAYINDEX | Shell32.SHGFI.SHGFI_ICON | Shell32.SHGFI.SHGFI_SYSICONINDEX | Shell32.SHGFI.SHGFI_ICONLOCATION);

            if (Result.CheckIfValidPtr())
            {
                User32.DestroyIcon(Shfi.hIcon);

                if (Shell32.SHGetImageList(Shell32.SHIL.SHIL_LARGE, typeof(ComCtl32.IImageList).GUID, out object PPV).Succeeded)
                {
                    using (ComCtl32.SafeHIMAGELIST ImageList = ComCtl32.SafeHIMAGELIST.FromIImageList((ComCtl32.IImageList)PPV))
                    {
                        if (!ImageList.IsInvalid)
                        {
                            int OverlayIndex = Shfi.iIcon >> 24;

                            if (OverlayIndex != 0)
                            {
                                int OverlayImage = ImageList.Interface.GetOverlayImage(OverlayIndex);

                                using (User32.SafeHICON OverlayIcon = ImageList.Interface.GetIcon(OverlayImage, ComCtl32.IMAGELISTDRAWFLAGS.ILD_PRESERVEALPHA))
                                {
                                    if (!OverlayIcon.IsInvalid)
                                    {
                                        using (Bitmap OriginBitmap = Bitmap.FromHicon(OverlayIcon.DangerousGetHandle()))
                                        using (MemoryStream MStream = new MemoryStream())
                                        {
                                            OriginBitmap.MakeTransparent(Color.Black);
                                            OriginBitmap.Save(MStream, ImageFormat.Png);

                                            return MStream.ToArray();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return Array.Empty<byte>();
        }

        public static string MTPGenerateUniquePath(MediaDevice Device, string Path, CreateType Type)
        {
            string UniquePath = Path;

            if (Device.FileExists(Path) || Device.DirectoryExists(Path))
            {
                string Name = Type == CreateType.Folder ? System.IO.Path.GetFileName(Path) : System.IO.Path.GetFileNameWithoutExtension(Path);
                string Extension = Type == CreateType.Folder ? string.Empty : System.IO.Path.GetExtension(Path);
                string DirectoryPath = System.IO.Path.GetDirectoryName(Path);

                for (ushort Count = 1; Device.DirectoryExists(UniquePath) || Device.FileExists(UniquePath); Count++)
                {
                    if (Regex.IsMatch(Name, @".*\(\d+\)"))
                    {
                        UniquePath = System.IO.Path.Combine(DirectoryPath, $"{Name.Substring(0, Name.LastIndexOf("(", StringComparison.InvariantCultureIgnoreCase))}({Count}){Extension}");
                    }
                    else
                    {
                        UniquePath = System.IO.Path.Combine(DirectoryPath, $"{Name} ({Count}){Extension}");
                    }
                }
            }

            return UniquePath;
        }

        public static string GenerateUniquePathOnLocal(string Path, CreateType Type)
        {
            string UniquePath = Path;

            if (File.Exists(Path) || Directory.Exists(Path))
            {
                string Name = Type == CreateType.Folder ? System.IO.Path.GetFileName(Path) : System.IO.Path.GetFileNameWithoutExtension(Path);
                string Extension = Type == CreateType.Folder ? string.Empty : System.IO.Path.GetExtension(Path);
                string DirectoryPath = System.IO.Path.GetDirectoryName(Path);

                for (ushort Count = 1; Directory.Exists(UniquePath) || File.Exists(UniquePath); Count++)
                {
                    if (Regex.IsMatch(Name, @".*\(\d+\)"))
                    {
                        UniquePath = System.IO.Path.Combine(DirectoryPath, $"{Name.Substring(0, Name.LastIndexOf("(", StringComparison.InvariantCultureIgnoreCase))}({Count}){Extension}");
                    }
                    else
                    {
                        UniquePath = System.IO.Path.Combine(DirectoryPath, $"{Name} ({Count}){Extension}");
                    }
                }
            }

            return UniquePath;
        }

        public static string ConvertShortPathToLongPath(string ShortPath)
        {
            int BufferSize = 512;

            StringBuilder Builder = new StringBuilder(BufferSize);

            uint ReturnNum = Kernel32.GetLongPathName(ShortPath, Builder, Convert.ToUInt32(BufferSize));

            if (ReturnNum > BufferSize)
            {
                BufferSize = Builder.EnsureCapacity(Convert.ToInt32(ReturnNum));

                if (Kernel32.GetLongPathName(ShortPath, Builder, Convert.ToUInt32(BufferSize)) > 0)
                {
                    return Builder.ToString();
                }
                else
                {
                    return ShortPath;
                }
            }
            else if (ReturnNum > 0)
            {
                return Builder.ToString();
            }
            else
            {
                return ShortPath;
            }
        }

        public static DateTimeOffset ConvertToLocalDateTimeOffset(FILETIME FileTime)
        {
            try
            {
                if (Kernel32.FileTimeToSystemTime(FileTime, out SYSTEMTIME ModTime))
                {
                    return new DateTime(ModTime.wYear, ModTime.wMonth, ModTime.wDay, ModTime.wHour, ModTime.wMinute, ModTime.wSecond, ModTime.wMilliseconds, DateTimeKind.Utc).ToLocalTime();
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"A exception was threw in {nameof(ConvertToLocalDateTimeOffset)}");
            }

            return default;
        }

        public static IEnumerable<HWND> GetCurrentWindowsHandles()
        {
            HWND Handle = HWND.NULL;

            while (true)
            {
                Handle = User32.FindWindowEx(HWND.NULL, Handle, null, null);

                if (Handle == HWND.NULL)
                {
                    break;
                }

                if (User32.IsWindowVisible(Handle) && !User32.IsIconic(Handle))
                {
                    yield return Handle;
                }
            }
        }

        public static string GetMIMEFromPath(string Path)
        {
            if (!File.Exists(Path))
            {
                throw new FileNotFoundException($"\"{Path}\" not found");
            }

            if (MimeTypeMap.TryGetMimeType(System.IO.Path.GetExtension(Path), out string Mime))
            {
                return Mime;
            }
            else
            {
                return "unknown/unknown";
            }
        }

        public static WindowInformation GetWindowInformationFromUwpApplication(uint TargetProcessId)
        {
            WindowInformation Info = null;

            if (TargetProcessId > 0)
            {
                User32.EnumWindowsProc Callback = new User32.EnumWindowsProc((WindowHandle, lParam) =>
                {
                    string ClassName = GetClassNameFromWindowHandle(WindowHandle.DangerousGetHandle());

                    if (!string.IsNullOrEmpty(ClassName))
                    {
                        HWND CoreWindowHandle = HWND.NULL;
                        HWND ApplicationFrameWindowHandle = HWND.NULL;

                        // Since User32.IsIconic is useless if the Window handle is belongs to Uwp
                        // Minimized : "Windows.UI.Core.CoreWindow" top window
                        // Normal : "Windows.UI.Core.CoreWindow" child of "ApplicationFrameWindow"
                        switch (ClassName)
                        {
                            case "ApplicationFrameWindow":
                                {
                                    ApplicationFrameWindowHandle = WindowHandle;
                                    CoreWindowHandle = User32.FindWindowEx(WindowHandle, lpszClass: "Windows.UI.Core.CoreWindow");
                                    break;
                                }
                            case "Windows.UI.Core.CoreWindow":
                                {
                                    CoreWindowHandle = WindowHandle;
                                    break;
                                }
                        }

                        if (!CoreWindowHandle.IsNull)
                        {
                            if (User32.GetWindowThreadProcessId(CoreWindowHandle, out uint ProcessId) > 0)
                            {
                                if (TargetProcessId == ProcessId)
                                {
                                    WindowState State = WindowState.Normal;

                                    if (ApplicationFrameWindowHandle == HWND.NULL)
                                    {
                                        State = WindowState.Minimized;
                                    }
                                    else if (User32.GetWindowRect(CoreWindowHandle, out RECT WindowRect))
                                    {
                                        if (User32.SystemParametersInfo(User32.SPI.SPI_GETWORKAREA, out RECT WorkAreaRect))
                                        {
                                            //If Window rect is equal or out of SPI_GETWORKAREA, it means it's maximized;
                                            if (WindowRect.left <= WorkAreaRect.left && WindowRect.top <= WorkAreaRect.top && WindowRect.right >= WorkAreaRect.right && WindowRect.bottom >= WorkAreaRect.bottom)
                                            {
                                                State = WindowState.Maximized;
                                            }
                                        }
                                    }

                                    Info = new WindowInformation(GetExecutablePathFromProcessId(ProcessId), ProcessId, State, ApplicationFrameWindowHandle.DangerousGetHandle(), CoreWindowHandle.DangerousGetHandle());

                                    return false;
                                }
                            }
                        }
                    }

                    return true;
                });

                User32.EnumWindows(Callback, IntPtr.Zero);
            }

            return Info;
        }

        public static string GetClassNameFromWindowHandle(IntPtr WindowHandle)
        {
            int NameLength = 256;

            StringBuilder Builder = new StringBuilder(NameLength);

            if (User32.GetClassName(WindowHandle, Builder, NameLength) > 0)
            {
                return Builder.ToString();
            }
            else
            {
                while (Win32Error.GetLastError() == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                {
                    Builder.EnsureCapacity(NameLength *= 2);

                    if (User32.GetClassName(WindowHandle, Builder, NameLength) > 0)
                    {
                        return Builder.ToString();
                    }
                }
            }

            return string.Empty;
        }

        public static string GetExecutablePathFromProcessId(uint ProcessId)
        {
            uint PathLength = 256;

            using (Kernel32.SafeHPROCESS ProcessHandle = Kernel32.OpenProcess(ACCESS_MASK.GENERIC_READ, false, ProcessId))
            {
                if (!ProcessHandle.IsInvalid)
                {
                    StringBuilder Builder = new StringBuilder((int)PathLength);

                    if (Kernel32.QueryFullProcessImageName(ProcessHandle, Kernel32.PROCESS_NAME.PROCESS_NAME_WIN32, Builder, ref PathLength))
                    {
                        return Builder.ToString();
                    }
                    else
                    {
                        while (Win32Error.GetLastError() == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                        {
                            Builder.EnsureCapacity((int)(PathLength *= 2));

                            if (Kernel32.QueryFullProcessImageName(ProcessHandle, Kernel32.PROCESS_NAME.PROCESS_NAME_WIN32, Builder, ref PathLength))
                            {
                                return Builder.ToString();
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        public static string GetPackageFamilyNameFromShellLink(string LinkPath)
        {
            try
            {
                using (ShellItem LinkItem = new ShellItem(LinkPath))
                {
                    string AUMID = LinkItem.Properties.GetPropertyString(Ole32.PROPERTYKEY.System.Link.TargetParsingPath);

                    uint PackageFamilyNameLength = 0;
                    uint PackageRelativeIdLength = 0;

                    if (Kernel32.ParseApplicationUserModelId(AUMID, ref PackageFamilyNameLength, null, ref PackageRelativeIdLength) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                    {
                        StringBuilder PackageFamilyNameBuilder = new StringBuilder(Convert.ToInt32(PackageFamilyNameLength));
                        StringBuilder PackageRelativeIdBuilder = new StringBuilder(Convert.ToInt32(PackageRelativeIdLength));

                        if (Kernel32.ParseApplicationUserModelId(AUMID, ref PackageFamilyNameLength, PackageFamilyNameBuilder, ref PackageRelativeIdLength, PackageRelativeIdBuilder).Succeeded)
                        {
                            return PackageFamilyNameBuilder.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the package family name from the shell link");
            }

            return string.Empty;
        }

        public static bool CheckIfPackageFamilyNameExist(string PackageFamilyName)
        {
            if (!string.IsNullOrWhiteSpace(PackageFamilyName))
            {
                try
                {
                    uint FullNameCount = 0;
                    uint FullNameBufferLength = 0;

                    return Kernel32.FindPackagesByPackageFamily(PackageFamilyName, Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_HEAD | Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_DIRECT, ref FullNameCount, IntPtr.Zero, ref FullNameBufferLength, IntPtr.Zero, IntPtr.Zero) == Win32Error.ERROR_INSUFFICIENT_BUFFER;
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not get the package full name from the package family name");
                }
            }

            return false;
        }

        public static bool LaunchApplicationFromPackageFamilyName(string PackageFamilyName, params string[] Arguments)
        {
            string AppUserModelId = GetAppUserModeIdFromPackageFullName(GetPackageFullNameFromPackageFamilyName(PackageFamilyName));

            if (!string.IsNullOrEmpty(AppUserModelId))
            {
                return LaunchApplicationFromAppUserModelId(AppUserModelId, Arguments);
            }

            return false;
        }

        public static bool LaunchApplicationFromAppUserModelId(string AppUserModelId, params string[] Arguments)
        {
            try
            {
                List<Exception> ExceptionList = new List<Exception>(3);

                Guid CLSID_ApplicationActivationManager = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
                Guid IID_IApplicationActivationManager = new Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D");

                if (Ole32.CoCreateInstance(CLSID_ApplicationActivationManager, null, Ole32.CLSCTX.CLSCTX_LOCAL_SERVER, IID_IApplicationActivationManager, out object ppv).Succeeded)
                {
                    Shell32.IApplicationActivationManager Manager = (Shell32.IApplicationActivationManager)ppv;

                    IEnumerable<string> AvailableArguments = Arguments.Where((Item) => !string.IsNullOrWhiteSpace(Item));

                    if (AvailableArguments.Any() && AvailableArguments.All((Item) => File.Exists(Item) || Directory.Exists(Item)))
                    {
                        IEnumerable<ShellItem> SItemList = AvailableArguments.Select((Item) => new ShellItem(Item));

                        try
                        {
                            using (ShellItemArray ItemArray = new ShellItemArray(SItemList))
                            {
                                try
                                {
                                    Manager.ActivateForFile(AppUserModelId, ItemArray.IShellItemArray, "open", out uint ProcessId);

                                    if (ProcessId > 0)
                                    {
                                        return true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ExceptionList.Add(ex);
                                }

                                try
                                {
                                    Manager.ActivateForProtocol(AppUserModelId, ItemArray.IShellItemArray, out uint ProcessId);

                                    if (ProcessId > 0)
                                    {
                                        return true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ExceptionList.Add(ex);
                                }
                            }
                        }
                        finally
                        {
                            SItemList.ForEach((Item) => Item.Dispose());
                        }
                    }

                    try
                    {
                        Manager.ActivateApplication(AppUserModelId, string.Join(' ', AvailableArguments.Select((Item) => $"\"{Item}\"")), Shell32.ACTIVATEOPTIONS.AO_NONE, out uint ProcessId);

                        if (ProcessId > 0)
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ExceptionList.Add(ex);
                    }
                }

                throw new AggregateException(ExceptionList);
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not launch the application from AUMID");
            }

            return false;
        }

        public static string GetInstalledUwpApplicationVersion(string PackageFullName)
        {
            Kernel32.PACKAGE_INFO_REFERENCE Reference = new Kernel32.PACKAGE_INFO_REFERENCE();

            if (Kernel32.OpenPackageInfoByFullName(PackageFullName, 0, ref Reference).Succeeded)
            {
                try
                {
                    uint PackageInfoLength = 0;

                    if (Kernel32.GetPackageInfo(Reference, (uint)(Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_HEAD | Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_DIRECT), ref PackageInfoLength, IntPtr.Zero, out _) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                    {
                        IntPtr Buffer = Marshal.AllocHGlobal(Convert.ToInt32(PackageInfoLength));

                        try
                        {
                            if (Kernel32.GetPackageInfo(Reference, (uint)(Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_HEAD | Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_DIRECT), ref PackageInfoLength, Buffer, out uint Count).Succeeded && Count > 0)
                            {
                                Kernel32.PACKAGE_VERSION.DUMMYSTRUCTNAME VersionParts = Marshal.PtrToStructure<Kernel32.PACKAGE_INFO>(Buffer).packageId.version.Parts;

                                return $"{VersionParts.Major}.{VersionParts.Minor}.{VersionParts.Build}.{VersionParts.Revision}";
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(Buffer);
                        }
                    }
                }
                finally
                {
                    Kernel32.ClosePackageInfo(Reference);
                }
            }

            return string.Empty;
        }

        public static InstalledApplicationPackage GetSpecificInstalledUwpApplication(string PackageFamilyName)
        {
            static string GetBestMatchLogoPath(string LogoPath)
            {
                int LargestSize = 0;

                string ReturnValue = LogoPath;
                string BaseDirectory = Path.GetDirectoryName(LogoPath);

                if (Directory.Exists(BaseDirectory))
                {
                    foreach (string TargetLogoPath in Directory.EnumerateFiles(BaseDirectory, $"{Path.GetFileNameWithoutExtension(LogoPath)}.scale-*{Path.GetExtension(LogoPath)}"))
                    {
                        string SizeText = Path.GetFileNameWithoutExtension(TargetLogoPath).Split(".scale-", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

                        if (int.TryParse(SizeText, out int Size) && Size > LargestSize)
                        {
                            ReturnValue = TargetLogoPath;
                        }
                    }
                }

                return ReturnValue;
            }

            if (!string.IsNullOrEmpty(PackageFamilyName))
            {
                try
                {
                    string PackageFullName = GetPackageFullNameFromPackageFamilyName(PackageFamilyName);

                    if (!string.IsNullOrEmpty(PackageFullName))
                    {
                        if (Kernel32.GetStagedPackageOrigin(PackageFullName, out Kernel32.PackageOrigin Origin).Succeeded)
                        {
                            switch (Origin)
                            {
                                case Kernel32.PackageOrigin.PackageOrigin_Inbox:
                                case Kernel32.PackageOrigin.PackageOrigin_Store:
                                case Kernel32.PackageOrigin.PackageOrigin_DeveloperSigned:
                                case Kernel32.PackageOrigin.PackageOrigin_LineOfBusiness:
                                    {
                                        string InstalledPath = GetInstalledPathFromPackageFullName(PackageFullName);

                                        if (!string.IsNullOrEmpty(InstalledPath))
                                        {
                                            string AppxManifestPath = Path.Combine(InstalledPath, "AppXManifest.xml");

                                            if (File.Exists(AppxManifestPath))
                                            {
                                                XmlDocument Document = new XmlDocument();

                                                using (XmlTextReader DocReader = new XmlTextReader(AppxManifestPath) { Namespaces = false })
                                                {
                                                    Document.Load(DocReader);

                                                    string Logo = Document.SelectSingleNode("/Package/Properties/Logo")?.InnerText;
                                                    string DisplayName = Document.SelectSingleNode("/Package/Properties/DisplayName")?.InnerText;
                                                    string PublisherDisplayName = Document.SelectSingleNode("/Package/Properties/PublisherDisplayName")?.InnerText;

                                                    if (Uri.TryCreate(DisplayName, UriKind.Absolute, out Uri DisplayNameResourceUri))
                                                    {
                                                        DisplayName = ExtractResourceFromPackageFullName(PackageFullName, DisplayNameResourceUri);
                                                    }

                                                    if (!string.IsNullOrEmpty(DisplayName))
                                                    {
                                                        if (Uri.TryCreate(PublisherDisplayName, UriKind.Absolute, out Uri PublisherDisplayNameResourceUri))
                                                        {
                                                            PublisherDisplayName = ExtractResourceFromPackageFullName(PackageFullName, PublisherDisplayNameResourceUri);
                                                        }

                                                        string LogoPath = GetBestMatchLogoPath(Path.Combine(InstalledPath, Logo));

                                                        if (File.Exists(LogoPath))
                                                        {
                                                            return new InstalledApplicationPackage(DisplayName, PublisherDisplayName, PackageFamilyName, File.ReadAllBytes(LogoPath));
                                                        }
                                                        else
                                                        {
                                                            return new InstalledApplicationPackage(DisplayName, PublisherDisplayName, PackageFamilyName, Array.Empty<byte>());
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        break;
                                    }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    //No need to handle this exception
                }
            }

            return null;
        }

        public static IReadOnlyList<InstalledApplicationPackage> GetAllInstalledUwpApplication()
        {
            HashSet<string> FilterAppFamilyName = new HashSet<string>(1)
            {
                "Microsoft.MicrosoftEdge_8wekyb3d8bbwe"
            };

            List<InstalledApplicationPackage> Result = new List<InstalledApplicationPackage>();

            try
            {
                using (ShellFolder AppsFolder = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_AppsFolder))
                {
                    foreach (ShellItem Item in AppsFolder.EnumerateChildren(FolderItemFilter.NonFolders))
                    {
                        try
                        {
                            string AUMID = Item.ParsingName;

                            if (!string.IsNullOrEmpty(AUMID))
                            {
                                uint PackageFamilyNameLength = 0;
                                uint PackageRelativeIdLength = 0;

                                if (Kernel32.ParseApplicationUserModelId(AUMID, ref PackageFamilyNameLength, null, ref PackageRelativeIdLength) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                                {
                                    StringBuilder PackageFamilyNameBuilder = new StringBuilder(Convert.ToInt32(PackageFamilyNameLength));
                                    StringBuilder PackageRelativeIdBuilder = new StringBuilder(Convert.ToInt32(PackageRelativeIdLength));

                                    if (Kernel32.ParseApplicationUserModelId(AUMID, ref PackageFamilyNameLength, PackageFamilyNameBuilder, ref PackageRelativeIdLength, PackageRelativeIdBuilder).Succeeded)
                                    {
                                        string PackageFamilyName = PackageFamilyNameBuilder.ToString();

                                        if (!FilterAppFamilyName.Contains(PackageFamilyName)
                                            && Result.All((Item) => !Item.AppFamilyName.Equals(PackageFamilyName, StringComparison.OrdinalIgnoreCase))
                                            && GetSpecificInstalledUwpApplication(PackageFamilyName) is InstalledApplicationPackage Package)
                                        {
                                            Result.Add(Package);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            //No need to handle this exception
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the installed uwp applications");
            }

            return Result;
        }

        public static string GetPackageFullNameFromPackageFamilyName(string PackageFamilyName)
        {
            if (!string.IsNullOrWhiteSpace(PackageFamilyName))
            {
                try
                {
                    uint FullNameCount = 0;
                    uint FullNameBufferLength = 0;

                    if (Kernel32.FindPackagesByPackageFamily(PackageFamilyName, Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_HEAD | Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_DIRECT, ref FullNameCount, IntPtr.Zero, ref FullNameBufferLength, IntPtr.Zero, IntPtr.Zero) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                    {
                        IntPtr PackageFullNamePtr = Marshal.AllocHGlobal(Convert.ToInt32(FullNameCount * IntPtr.Size));
                        IntPtr PackageFullNameBufferPtr = Marshal.AllocHGlobal(Convert.ToInt32(FullNameBufferLength * 2));

                        try
                        {
                            if (Kernel32.FindPackagesByPackageFamily(PackageFamilyName, Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_HEAD | Kernel32.PACKAGE_FLAGS.PACKAGE_FILTER_DIRECT, ref FullNameCount, PackageFullNamePtr, ref FullNameBufferLength, PackageFullNameBufferPtr, IntPtr.Zero).Succeeded)
                            {
                                return Marshal.PtrToStringUni(Marshal.ReadIntPtr(PackageFullNamePtr));
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(PackageFullNamePtr);
                            Marshal.FreeHGlobal(PackageFullNameBufferPtr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not get the package full name from the package family name");
                }
            }

            return string.Empty;
        }

        public static string GetAppUserModeIdFromPackageFullName(string PackageFullName)
        {
            try
            {
                if (!string.IsNullOrEmpty(PackageFullName))
                {
                    try
                    {
                        Kernel32.PACKAGE_INFO_REFERENCE Reference = new Kernel32.PACKAGE_INFO_REFERENCE();

                        if (Kernel32.OpenPackageInfoByFullName(PackageFullName, 0, ref Reference).Succeeded)
                        {
                            try
                            {
                                uint AppIdBufferLength = 0;

                                if (Kernel32.GetPackageApplicationIds(Reference, ref AppIdBufferLength, IntPtr.Zero, out _) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                                {
                                    IntPtr AppIdBufferPtr = Marshal.AllocHGlobal(Convert.ToInt32(AppIdBufferLength));

                                    try
                                    {
                                        if (Kernel32.GetPackageApplicationIds(Reference, ref AppIdBufferLength, AppIdBufferPtr, out uint AppIdCount).Succeeded && AppIdCount > 0)
                                        {
                                            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(AppIdBufferPtr));
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(AppIdBufferPtr);
                                    }
                                }
                            }
                            finally
                            {
                                Kernel32.ClosePackageInfo(Reference);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // No need to handle this exception
                    }

                    string InstalledPath = GetInstalledPathFromPackageFullName(PackageFullName);

                    if (!string.IsNullOrEmpty(InstalledPath))
                    {
                        string AppxManifestPath = Path.Combine(InstalledPath, "AppXManifest.xml");

                        if (File.Exists(AppxManifestPath))
                        {
                            XmlDocument Document = new XmlDocument();

                            using (XmlTextReader DocReader = new XmlTextReader(AppxManifestPath) { Namespaces = false })
                            {
                                Document.Load(DocReader);

                                string AppId = Document.SelectSingleNode("/Package/Applications/Application")?.Attributes?.GetNamedItem("Id")?.InnerText;

                                if (!string.IsNullOrEmpty(AppId))
                                {
                                    return $"{GetPackageFamilyNameFromPackageFullName(PackageFullName)}!{AppId}";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the AUMID from the package full name");
            }

            return string.Empty;
        }

        public static string GetInstalledPathFromPackageFullName(string PackageFullName)
        {
            try
            {
                uint PathLength = 0;

                if (Kernel32.GetStagedPackagePathByFullName(PackageFullName, ref PathLength) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                {
                    StringBuilder Builder = new StringBuilder(Convert.ToInt32(PathLength));

                    if (Kernel32.GetStagedPackagePathByFullName(PackageFullName, ref PathLength, Builder).Succeeded)
                    {
                        return Builder.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get installed path from package full name");
            }

            return string.Empty;
        }

        public static string GetPackageNameFromPackageFamilyName(string PackageFamilyName)
        {
            try
            {
                if (!string.IsNullOrEmpty(PackageFamilyName))
                {
                    uint PackageNameLength = 0;
                    uint PackagePublisherIdLength = 0;

                    if (Kernel32.PackageNameAndPublisherIdFromFamilyName(PackageFamilyName, ref PackageNameLength, null, ref PackagePublisherIdLength) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                    {
                        StringBuilder PackageNameBuilder = new StringBuilder(Convert.ToInt32(PackageNameLength));
                        StringBuilder PackagePublisherIdBuilder = new StringBuilder(Convert.ToInt32(PackagePublisherIdLength));

                        if (Kernel32.PackageNameAndPublisherIdFromFamilyName(PackageFamilyName, ref PackageNameLength, PackageNameBuilder, ref PackagePublisherIdLength, PackagePublisherIdBuilder).Succeeded)
                        {
                            return PackageNameBuilder.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the AUMID from the package family name");
            }

            return string.Empty;
        }

        public static string GetPackageFamilyNameFromPackageFullName(string PackageFullName)
        {
            if (!string.IsNullOrEmpty(PackageFullName))
            {
                try
                {
                    uint NameLength = 0;

                    if (Kernel32.PackageFamilyNameFromFullName(PackageFullName, ref NameLength) == Win32Error.ERROR_INSUFFICIENT_BUFFER)
                    {
                        StringBuilder Builder = new StringBuilder(Convert.ToInt32(NameLength));

                        if (Kernel32.PackageFamilyNameFromFullName(PackageFullName, ref NameLength, Builder).Succeeded)
                        {
                            return Builder.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not get the package family name from package full name");
                }
            }

            return string.Empty;
        }

        public static string ExtractResourceFromPackageFullName(string PackageFullName, Uri ResourceUri)
        {
            static string ExtractResourceCore(string PackageFullName, string Resource)
            {
                StringBuilder Builder = new StringBuilder(1024);

                if (ShlwApi.SHLoadIndirectString($"@{{{PackageFullName}?{Resource}}}", Builder, Convert.ToUInt32(Builder.Capacity)).Succeeded)
                {
                    return Builder.ToString();
                }

                return string.Empty;
            }

            if (!string.IsNullOrEmpty(PackageFullName))
            {
                try
                {
                    string PackageName = GetPackageNameFromPackageFamilyName(GetPackageFamilyNameFromPackageFullName(PackageFullName));

                    if (!string.IsNullOrEmpty(PackageName))
                    {
                        string ExtractedValue = ExtractResourceCore(PackageFullName, $"ms-resource://{PackageName}/resources/{ResourceUri.Segments.LastOrDefault()}");

                        if (string.IsNullOrEmpty(ExtractedValue))
                        {
                            ExtractedValue = ExtractResourceCore(PackageFullName, $"ms-resource://{PackageName}/{string.Join("/", ResourceUri.Segments.Select((Seg) => Seg.Trim('/')).Where((Seg) => !string.IsNullOrEmpty(Seg)))}");
                        }

                        return ExtractedValue;
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not extract the resource from pri file");
                }
            }

            return string.Empty;
        }

        public static string GetDefaultUwpPackageInstallationRoot()
        {
            using (RegistryKey Key = Registry.LocalMachine.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Appx"))
            {
                return Convert.ToString(Key.GetValue("PackageRoot"));
            }
        }
    }
}
