# Third-party software notices

ForMatthias itself is GPL-3.0-only. The following unmodified dependencies retain
their own MIT licenses; they are not relicensed as exclusively GPL software.
`SOURCE.md` identifies component versions and their source revisions, including
both MSBuild hosts. Files under `licenses/` preserve the upstream packages'
original notices, including Roslyn's `ThirdPartyNotices.rtf` and the .NET
packages' `THIRD-PARTY-NOTICES.TXT`. Those additional notices remain applicable.

## Humanizer.Core 2.14.1

Source: https://github.com/Humanizr/Humanizer/tree/3ebc38de585fc641a04b0e78ed69468453b0f8a1

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

Humanizer's upstream license additionally credits:

- Inflector (https://github.com/srkirkland/Inflector), MIT License,
  Copyright (c) 2013 Scott Kirkland.
- ByteSize (https://github.com/omar/ByteSize), MIT License,
  Copyright (c) 2013-2014 Omar Khudeira (http://omar.io).

## Microsoft and .NET components

Includes Roslyn (`Microsoft.CodeAnalysis.*`), MSBuild Locator,
`Microsoft.VisualStudio.SolutionPersistence`, `Microsoft.Extensions.*`,
`System.Composition.*`, and embedded host dependencies (`Microsoft.Bcl.AsyncInterfaces`,
`Microsoft.IO.Redist`, `System.Buffers`, `System.IO.Pipelines`, `System.Memory`,
`System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`,
`System.Text.Encodings.Web`, `System.Text.Json`, `System.Threading.Tasks.Extensions`).

Copyright (c) .NET Foundation and Contributors. All rights reserved.
Copyright (c) Microsoft Corporation. All rights reserved.

The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Upstream projects: https://github.com/dotnet/roslyn,
https://github.com/microsoft/MSBuildLocator,
https://github.com/microsoft/vs-solutionpersistence,
https://github.com/dotnet/runtime, https://github.com/dotnet/maintenance-packages
and https://github.com/dotnet/dotnet.
See `SOURCE.md` for version-specific source archive URLs.
