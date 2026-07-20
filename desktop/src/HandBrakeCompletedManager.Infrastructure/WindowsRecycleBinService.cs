using System.Runtime.InteropServices;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class WindowsRecycleBinService : IRecoverableFileRecycler
{
    private const uint FofSilent = 0x0004;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private static readonly Guid FileOperationClassId = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid ShellItemInterfaceId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Recycle Bin operations require Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The source file no longer exists at the recorded path.", fullPath);
        }

        IFileOperationNative? operation = null;
        IShellItemNative? item = null;
        try
        {
            var operationType = Type.GetTypeFromCLSID(FileOperationClassId, throwOnError: true)!;
            operation = (IFileOperationNative)Activator.CreateInstance(operationType)!;
            ThrowIfFailed(SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ShellItemInterfaceId, out item));
            ThrowIfFailed(operation.SetOperationFlags(
                FofSilent | FofNoConfirmation | FofNoErrorUi | FofxRecycleOnDelete | FofxEarlyFailure));
            ThrowIfFailed(operation.DeleteItem(item, IntPtr.Zero));
            ThrowIfFailed(operation.PerformOperations());
            ThrowIfFailed(operation.GetAnyOperationsAborted(out var aborted));
            if (aborted)
            {
                throw new OperationCanceledException("Windows aborted the Recycle Bin operation.");
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                throw new IOException("Windows did not move the source to the Recycle Bin.");
            }

            return Task.CompletedTask;
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
        }
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemNative shellItem);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemNative
    {
    }

    [ComImport]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationNative
    {
        [PreserveSig] int Advise(IntPtr progressSink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint operationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr progressDialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(uint ownerWindow);
        [PreserveSig] int ApplyPropertiesToItem(IShellItemNative item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItemNative item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr progressSink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IShellItemNative item, IShellItemNative destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr progressSink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItemNative destinationFolder);
        [PreserveSig] int CopyItem(IShellItemNative item, IShellItemNative destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string copyName, IntPtr progressSink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItemNative destinationFolder);
        [PreserveSig] int DeleteItem(IShellItemNative item, IntPtr progressSink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItemNative destinationFolder, uint fileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string templateName, IntPtr progressSink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool operationsAborted);
    }
}
