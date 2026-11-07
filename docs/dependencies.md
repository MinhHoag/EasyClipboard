# Dependencies and packaging

The prototype uses C#/.NET 10, Windows Forms, ASP.NET Core's Kestrel server, and Windows SDK C#/WinRT projections. It uses no QR-code, hotspot utility, test-framework, or clipboard package. Microsoft documentation and its sample informed the API design; sample source was not copied into this project.

The target is `net10.0-windows10.0.22621.0`. `global.json` specifies SDK 10.0.403 with feature-band roll-forward. The SDK resolved `Microsoft.Windows.SDK.NET.Ref` 10.0.22621.57 in this build. Its metadata requires license acceptance and links the [Windows SDK license](https://aka.ms/WinSDKLicenseURL). Do not assume all SDK components fall under the repository's future source license.

The framework-dependent build needs .NET 10 Desktop Runtime and ASP.NET Core Runtime on Windows; the local SDK includes both for development. The receiver launchers use its Windows executable and set DOTNET_ROOT when using the workspace SDK. The older diagnostic tool uses the dotnet host directly. Use the provided launchers during development.

No installer, release archive, bundled runtime, or public repository has been created. Before distribution:

1. Choose the project license with the user; it remains undecided.
2. Inventory the exact publish output, including SDK/CsWinRT runtime assemblies and applicable licenses/notices.
3. Review [Microsoft's .NET license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), ASP.NET Core notices, and Windows SDK redistribution terms for the selected package format.
4. Decide between framework-dependent and self-contained packaging. Bundled runtimes need redistribution notices and a security-update plan.
5. Exclude `.tools`, caches, certificates, credentials, test results, and user history from release packages.
6. Validate on a clean supported Windows installation and scan for secrets. Application/installer code signing is not configured yet.

The initial SDK restore emitted a standard development HTTPS-certificate creation message. Neither app uses that certificate or enables Windows/iPhone certificate trust automatically. Scripts suppress that first-run behavior. The receiver generates a separate per-installation CA and server certificate for its documented manual iPhone trust setup. Test identities remain in ignored `.tools` and are never release inputs.
