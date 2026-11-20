# Upgrade path: net8.0 to net10.0

The port was first written for **.NET 8 (LTS)** and then upgraded to **.NET 10 (LTS)**. .NET 8 reached end of
support on 10 November 2026, and .NET 10 is supported until November 2028. .NET 11 (released November 2026) is a
Standard Term Support release, so it was not chosen as the production target.

The repository deliberately still **multi-targets `net8.0;net10.0`**. CI builds and tests both, which proves the
upgrade changed nothing: the same unit, integration and black-box parity suites pass on both runtimes. In a real
project the `net8.0` target would be removed once the release on .NET 10 is signed off (step 8).

## Steps

### 1. Baseline on the old framework
All suites green on `net8.0`, including the PHP parity suite. Without a baseline you cannot tell an upgrade
regression from an existing bug.

### 2. Pin the SDK with `global.json`
```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature", "allowPrerelease": false } }
```
Every machine and CI runner now uses a .NET 10 SDK (any 10.0 feature band / patch at or above 10.0.100), even
when a newer major SDK (11.0) is installed. One 10.0 SDK builds both target frameworks.

### 3. Change the target framework
In [`Directory.Build.props`](../Directory.Build.props):
```xml
<OrdersTargetFrameworks>net8.0;net10.0</OrdersTargetFrameworks>   <!-- was: net8.0 -->
```
Projects use `<TargetFrameworks>$(OrdersTargetFrameworks)</TargetFrameworks>`, so the change is one line.
New code with no legacy counterpart (the Azure Functions app) targets `net10.0` only.

`LangVersion` stays at `12.0` while both frameworks are built, so the exact same C# compiles for both
(.NET 10 would default to C# 14).

### 4. Update packages
[Central Package Management](../Directory.Packages.props) keeps every version in one file.
- **Framework-aligned packages** get one version per target framework:
  ```xml
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.34" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.15" />
  </ItemGroup>
  ```
- **Framework-independent packages** (Azure SDK, OpenTelemetry, xUnit, Reqnroll) use a single version that
  supports both targets.
- Libraries reference `Microsoft.AspNetCore.App` as a `FrameworkReference` instead of individual
  `Microsoft.Extensions.*` packages, so there are fewer versions to align.
- Dependabot ignores major updates of framework-aligned packages (they follow the target framework, not the calendar).

All versions were checked on nuget.org in November 2026 (latest stable at the time).

### 5. Check breaking changes
Read the official lists for every version crossed:
[.NET 9](https://learn.microsoft.com/dotnet/core/compatibility/9.0) and
[.NET 10](https://learn.microsoft.com/dotnet/core/compatibility/10.0). Then search the code base for the APIs they
mention. Items checked for this code base:

| Area | Finding | Action |
|---|---|---|
| `System.Text.Json` | `JsonSerializerOptions.Web` exists only from .NET 9 | Use `new JsonSerializerOptions(JsonSerializerDefaults.Web)` while net8.0 is still built |
| Minimal APIs / `Program` | .NET 10 can generate a `public partial class Program` for top-level statements | Keep the explicit declaration until net8.0 is dropped (needed by `WebApplicationFactory<Program>` on .NET 8) |
| Solution format | `.slnx` needs a recent SDK; the .NET 10 SDK creates it by default | `AzureDotnetMigration.slnx`, SDK pinned by `global.json` |
| NuGet audit | Vulnerability audit of transitive packages | `NuGetAudit` + `NuGetAuditMode=all` set explicitly in `Directory.Build.props`, so both targets report the same |
| `dotnet test` | The .NET 10 SDK can run tests on Microsoft.Testing.Platform | Stays on VSTest (no `test` runner in `global.json`) so TRX output is unchanged |
| Serialization of money | `decimal` output identical on both runtimes | Covered by golden files (numbers compared by value) |

### 6. Compare analyzer warnings
Build each target with warnings visible and compare:
```bash
dotnet build -c Release -f net8.0  -p:TreatWarningsAsErrors=false 2>&1 | grep -E "warning (CA|IDE|ASP|SYSLIB)" | sort -u > warn-net8.txt
dotnet build -c Release -f net10.0 -p:TreatWarningsAsErrors=false 2>&1 | grep -E "warning (CA|IDE|ASP|SYSLIB)" | sort -u > warn-net10.txt
diff warn-net8.txt warn-net10.txt
```
New `SYSLIB` obsoletion warnings point at APIs that will break later; fix them before dropping the old target.

### 7. Prove behaviour is unchanged
CI matrix `tfm: [net8.0, net10.0]` runs, for each runtime:
- unit tests (`dotnet test -f <tfm>`),
- integration tests (WebApplicationFactory + Azurite, `-f <tfm>`),
- the black-box parity suite against the API **running on that runtime**
  (`dotnet src/Orders.Api/bin/Release/<tfm>/Orders.Api.dll`) and the legacy PHP API.

Green on both = same behaviour on both. The HTML report lists each suite per framework side by side.

### 8. Drop the old target (after release sign-off)
- `OrdersTargetFrameworks` becomes `net10.0`.
- Remove the `net8.0` package conditions and the `LangVersion` pin.
- Remove the explicit `public partial class Program`.
- Switch to `JsonSerializerOptions.Web` if wanted.
- Remove `net8.0` from the CI matrix.
