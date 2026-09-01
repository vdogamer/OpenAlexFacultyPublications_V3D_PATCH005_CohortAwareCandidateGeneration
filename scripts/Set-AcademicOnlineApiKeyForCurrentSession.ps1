$secureApiKey = Read-Host "Paste the Academic Online API key" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureApiKey)

try {
    $plainApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)

    if ([string]::IsNullOrWhiteSpace($plainApiKey)) {
        throw "Academic Online API key was empty. Environment variable was not changed."
    }

    $env:AcademicOnline__ApiKey = $plainApiKey
    Write-Host "AcademicOnline__ApiKey is set for this PowerShell session. The key was not printed or written to the repository."
}
finally {
    if ($bstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    $plainApiKey = $null
    $secureApiKey = $null
}
