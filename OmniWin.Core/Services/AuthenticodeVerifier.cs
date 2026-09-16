using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace OmniWin.Core.Services;

/// <summary>
/// AuthenticodeVerifier: Native Win32 cryptographic trust verification (WinVerifyTrust)
/// and X509 digital certificate inspection for PE binaries, drivers (.sys), and installers.
/// </summary>
public static class AuthenticodeVerifier
{
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x00000100;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWVTData);

    /// <summary>
    /// Cryptographically verifies the Authenticode digital signature of any binary, installer or driver file.
    /// Returns signer name and verifies that the file has not been altered or tampered with.
    /// </summary>
    public static AuthenticodeSignatureResult VerifyFileSignature(string filePath)
    {
        var res = new AuthenticodeSignatureResult();
        if (!File.Exists(filePath))
        {
            res.StatusMessage = "File not found.";
            return res;
        }

        // 1. Check certificate details via X509Certificate.CreateFromSignedFile
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            res.IsSigned = true;
            res.SignerName = cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject;
            res.IssuerName = cert.GetNameInfo(X509NameType.SimpleName, true) ?? cert.Issuer;
            res.NotBefore = cert.NotBefore;
            res.NotAfter = cert.NotAfter;
        }
        catch
        {
            // Not a PKCS#7 signed file or corrupted signature
            res.IsSigned = false;
            res.IsTrusted = false;
            res.StatusMessage = "Unsigned or corrupted signature.";
            return res;
        }

        // 2. Cryptographic trust validation via native WinVerifyTrust
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var trustData = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = WTD_CHOICE_FILE,
            dwStateAction = WTD_STATEACTION_IGNORE,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = null,
            dwProvFlags = WTD_SAFER_FLAG,
            dwUIContext = 0
        };

        IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        IntPtr pTrustData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());

        try
        {
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);
            trustData.pFile = pFileInfo;
            Marshal.StructureToPtr(trustData, pTrustData, false);

            uint result = WinVerifyTrust(INVALID_HANDLE_VALUE, WINTRUST_ACTION_GENERIC_VERIFY_V2, pTrustData);

            // 0x00000000 = ERROR_SUCCESS (Verified and trusted)
            if (result == 0)
            {
                res.IsTrusted = true;
                res.StatusMessage = "Verified & Trusted (WinVerifyTrust OK).";
            }
            else
            {
                res.IsTrusted = false;
                res.StatusMessage = $"Untrusted or invalid signature (WinVerifyTrust code: 0x{result:X8}).";
            }
        }
        catch (Exception ex)
        {
            res.IsTrusted = false;
            res.StatusMessage = $"WinVerifyTrust verification error: {ex.Message}";
        }
        finally
        {
            Marshal.FreeHGlobal(pFileInfo);
            Marshal.FreeHGlobal(pTrustData);
        }

        return res;
    }
}
