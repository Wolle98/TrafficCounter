# ADR-0044: Additional AGPL permission for proprietary runtime libraries

- **Status:** accepted
- **Date:** 2026-10-23
- **Stage:** 0
- **Extends:** [ADR-0004](0004-license-agpl.md)

## Context

The repository is to be made public under AGPL-3.0 (ADR-0004). All NuGet dependencies are under licenses compatible with it (MIT, Apache-2.0, BSD, PostgreSQL), except some native runtime libraries the packages bring along:

- Microsoft DirectML (`Microsoft.AI.DirectML`, via `Microsoft.ML.OnnxRuntime.DirectML`, ADR-0007);
- the `Microsoft.Data.SqlClient` SNI runtime and `Microsoft.Identity.Client.NativeInterop` (via EF Core SQL Server, ADR-0010);
- NVIDIA CUDA, cuDNN and TensorRT for the NVIDIA build (ADR-0041).

Their licenses allow redistribution with an application but add terms the AGPL does not allow for conveyed object code. Publishing the source is not affected; conveying built packages would be.

## Options considered

1. Leave it: fine as long as nobody conveys built packages.
2. Drop the libraries: no DirectML or NVIDIA acceleration, no SQL Server.
3. **Additional permission under AGPL-3.0 section 7** for exactly these libraries, given by the copyright holder.

## Decision

Option 3. The README states the copyright (pseudonym `Wolle98`) and the additional permission: a covered work may be conveyed combined with the libraries listed above; its Corresponding Source does not include their source. Built packages are not published; whoever conveys them passes on the third-party notices (Apache-2.0 NOTICE files, LGPL notice of the FFmpeg library shipped with OpenCV).

## Consequences

- Positive: the program with all its backends can be passed on without a licence conflict.
- Negative: further proprietary libraries need an amendment of the permission (a new ADR).
- Contributions from others would have to be made under the same permission.

## Implementation note (2026-10-23)

The licenses differ in how far they allow redistribution. DirectML and the SqlClient SNI runtime are distributable code that may be shipped with an application. `Microsoft.Identity.Client.NativeInterop` (`msalruntime.dll`) may not be shared or distributed to any third party at all (its section 3 e), so the additional permission cannot help there: a package conveyed to others must leave `msalruntime.dll` out. `Microsoft.Data.SqlClient` only needs it for Microsoft Entra ID authentication, which this project does not use.
