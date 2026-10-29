using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexo
{
    /// <summary>
    /// Selector de carpetas moderno de Windows (IFileOpenDialog con FOS_PICKFOLDERS). WPF en .NET Framework 4.8
    /// no trae uno y el FolderBrowserDialog de Windows Forms es el árbol antiguo.
    /// </summary>
    internal static class FolderPicker
    {
        /// <summary>Ruta elegida, o null si el usuario cancela o la elección no es una carpeta del sistema de archivos.</summary>
        public static string Pick(Window owner, string title, string initialFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogCom();
            try
            {
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosNoChangeDir | FosPathMustExist);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Usar esta carpeta");
                if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref iid, out var item) == 0 && item != null)
                    {
                        dialog.SetFolder(item);
                        Marshal.ReleaseComObject(item);
                    }
                }
                var hwnd = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
                int hr = dialog.Show(hwnd);
                if (hr != 0) return null; // ERROR_CANCELLED u otro fallo: no se elige nada
                dialog.GetResult(out var result);
                try
                {
                    result.GetDisplayName(SigdnFileSysPath, out IntPtr name);
                    try { return Marshal.PtrToStringUni(name); }
                    finally { Marshal.FreeCoTaskMem(name); }
                }
                finally { Marshal.ReleaseComObject(result); }
            }
            catch (COMException) { return null; }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        private const uint FosNoChangeDir = 0x8, FosPickFolders = 0x20, FosForceFileSystem = 0x40, FosPathMustExist = 0x800;
        private const uint SigdnFileSysPath = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCom { }

        // IModalWindow → IFileDialog → IFileOpenDialog, en el orden exacto de la vtable.
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
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
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
