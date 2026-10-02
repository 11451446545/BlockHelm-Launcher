// IFileOpenDialog is available from Windows Vista onward. Use the same native
// folder picker as WPF OpenFolderDialog rather than the legacy tree-only dialog.
#if NET6_0
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Launcher.App.Compatibility;

internal static class FolderPickerCompatibility
{
    private const uint PickFolders = 0x20;
    private const uint ForceFileSystem = 0x40;
    private const uint PathMustExist = 0x800;
    private const uint FileSystemPath = 0x80058000;
    private const int Cancelled = unchecked((int)0x800704C7);

    public static string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        IShellItem? initialFolder = null;
        IShellItem? selectedFolder = null;
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
            dialog.SetTitle(title);
            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                var path = Path.GetFullPath(initialDirectory);
                if (Directory.Exists(path))
                {
                    var iid = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out initialFolder);
                    dialog.SetFolder(initialFolder);
                }
            }
            var owner = System.Windows.Application.Current?.MainWindow;
            var result = dialog.Show(owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle);
            if (result == Cancelled) return null;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out selectedFolder);
            selectedFolder.GetDisplayName(FileSystemPath, out var pointer);
            try { return Marshal.PtrToStringUni(pointer); }
            finally { Marshal.FreeCoTaskMem(pointer); }
        }
        finally
        {
            if (selectedFolder is not null) Marshal.FinalReleaseComObject(selectedFolder);
            if (initialFolder is not null) Marshal.FinalReleaseComObject(initialFolder);
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog { }

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem result);
        void AddPlace(IShellItem item, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr results);
        void GetSelectedItems(out IntPtr results);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint format, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
#endif
