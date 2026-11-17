# Third-party notices

The custom [HolyBeat License](LICENSE) applies only to original HolyBeat
materials owned by the project rights holder. It does not replace or restrict
the separate licenses for the third-party packages listed below.

These are the direct and transitive NuGet packages resolved for the project and
test suite on November 16, 2026. Package dependencies are restored by NuGet;
their source and package binaries are not vendored in this repository. The
package licenses continue to apply to those packages.

## MIT-licensed packages

| Packages and resolved versions | Copyright / attribution | License |
| --- | --- | --- |
| `NAudio`, `NAudio.Core`, `NAudio.Midi`, `NAudio.Wasapi`, `NAudio.WinMM` 3.3.0 | Copyright 2020 Mark Heath; NAudio contributors | [MIT license](https://github.com/naudio/NAudio/blob/master/license.txt) |
| `System.Numerics.Tensors` 9.0.0 | .NET Foundation and Contributors | [MIT license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| `Microsoft.NET.Test.Sdk`, `Microsoft.CodeCoverage`, `Microsoft.TestPlatform.ObjectModel`, `Microsoft.TestPlatform.TestHost` 17.14.1 (test dependencies) | Microsoft Corporation | [MIT license](https://github.com/microsoft/vstest/blob/main/LICENSE) |
| `coverlet.collector` 6.0.4 (test dependency) | Copyright 2018 Toni Solarin-Sodara | [MIT license](https://github.com/coverlet-coverage/coverlet/blob/master/LICENSE) |
| `Newtonsoft.Json` 13.0.3 (test dependency) | Copyright 2007 James Newton-King | [MIT license](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md) |

## Apache-2.0-licensed packages

| Packages and resolved versions | Copyright / attribution | License |
| --- | --- | --- |
| `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `xunit.analyzers` 1.18.0, `xunit.assert`, `xunit.core`, `xunit.extensibility.core`, `xunit.extensibility.execution` 2.9.3, and `xunit.abstractions` 2.0.3 (test dependencies) | Copyright .NET Foundation and Contributors | [Upstream xUnit license notice](https://github.com/xunit/xunit/blob/master/license.txt); [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0) |

The xUnit upstream license notice also identifies imported components that
retain their own MIT notices. Follow the upstream notice for those components.

## Notes

- Package versions above record the resolved dependency set at the date stated;
  future restores may resolve different transitive versions.
- The Windows/.NET SDK and framework are build/runtime prerequisites, not
  vendored packages in this source repository. Review Microsoft's applicable
  terms when distributing a self-contained application build.
- Reference audio, generated build output, and personal files are not included
  in this repository.
