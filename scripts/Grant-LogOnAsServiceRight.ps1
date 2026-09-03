[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AccountName
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator before granting 'Log on as a service'."
}

if ($AccountName.StartsWith(".\")) {
    $AccountName = "$env:COMPUTERNAME\$($AccountName.Substring(2))"
}

if (-not ("OpenAlex.Native.LsaAccountRights" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OpenAlex.Native
{
    public static class LsaAccountRights
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public LSA_UNICODE_STRING ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [DllImport("advapi32.dll", PreserveSig = true)]
        private static extern uint LsaOpenPolicy(
            ref LSA_UNICODE_STRING SystemName,
            ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
            uint DesiredAccess,
            out IntPtr PolicyHandle);

        [DllImport("advapi32.dll", PreserveSig = true)]
        private static extern uint LsaAddAccountRights(
            IntPtr PolicyHandle,
            IntPtr AccountSid,
            LSA_UNICODE_STRING[] UserRights,
            uint CountOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaClose(IntPtr ObjectHandle);

        [DllImport("advapi32.dll")]
        private static extern uint LsaNtStatusToWinError(uint Status);

        private const uint POLICY_LOOKUP_NAMES = 0x00000800;
        private const uint POLICY_CREATE_ACCOUNT = 0x00000010;

        public static void GrantLogOnAsService(string accountName)
        {
            var account = new NTAccount(accountName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));

            var sidBytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidBytes, 0);

            IntPtr sidPointer = IntPtr.Zero;
            IntPtr rightBuffer = IntPtr.Zero;
            IntPtr policyHandle = IntPtr.Zero;

            try
            {
                sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);

                const string rightName = "SeServiceLogonRight";
                rightBuffer = Marshal.StringToHGlobalUni(rightName);

                var right = new LSA_UNICODE_STRING
                {
                    Buffer = rightBuffer,
                    Length = (ushort)(rightName.Length * 2),
                    MaximumLength = (ushort)((rightName.Length + 1) * 2)
                };

                var systemName = new LSA_UNICODE_STRING();

                var attributes = new LSA_OBJECT_ATTRIBUTES
                {
                    Length = Marshal.SizeOf(typeof(LSA_OBJECT_ATTRIBUTES))
                };

                var status = LsaOpenPolicy(
                    ref systemName,
                    ref attributes,
                    POLICY_LOOKUP_NAMES | POLICY_CREATE_ACCOUNT,
                    out policyHandle);

                ThrowIfLsaError(status, "LsaOpenPolicy");

                status = LsaAddAccountRights(
                    policyHandle,
                    sidPointer,
                    new[] { right },
                    1);

                ThrowIfLsaError(status, "LsaAddAccountRights");
            }
            finally
            {
                if (policyHandle != IntPtr.Zero)
                {
                    LsaClose(policyHandle);
                }

                if (sidPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(sidPointer);
                }

                if (rightBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(rightBuffer);
                }
            }
        }

        private static void ThrowIfLsaError(uint status, string operation)
        {
            if (status == 0)
            {
                return;
            }

            var win32Error = (int)LsaNtStatusToWinError(status);
            throw new Win32Exception(win32Error, operation + " failed.");
        }
    }
}
"@
}

[OpenAlex.Native.LsaAccountRights]::GrantLogOnAsService($AccountName)

Write-Host "Granted 'Log on as a service' to:"
Write-Host "  $AccountName"
