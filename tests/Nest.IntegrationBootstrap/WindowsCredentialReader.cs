using System.Runtime.InteropServices;

internal static class WindowsCredentialReader
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr pointer);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr pointer);

    public static string ReadPassword(string user)
    {
        if (!CredRead("Nest/IntegrationTest/MariaDB/localhost:3306/" + user, 1, 0, out var pointer))
            throw new InvalidOperationException("Die Testzugangsdaten fehlen im Windows-Anmeldeinformationsmanager.");
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (Marshal.PtrToStringUni(credential.UserName) != user ||
                credential.CredentialBlobSize == 0 || credential.CredentialBlobSize % 2 != 0)
                throw new InvalidOperationException("Die gespeicherten Testzugangsdaten sind ungültig.");
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2))
                ?? throw new InvalidOperationException("Das Testkennwort fehlt.");
        }
        finally { CredFree(pointer); }
    }
}
