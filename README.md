# Devlead.SourcePack

Opinionated MSBuild package for authoring **source NuGet packages** using standard SDK `dotnet pack`.

## Features

- Ships `.cs` files under `contentFiles/cs/{tfm}/...` with `BuildAction=Compile`
- Optional `build/{tfm}/*.props` and `*.targets` via `SourcePackFile`
- Optional `SourcePackBundle` for meta-packages that re-export dependency source/build assets and promote that package's **direct** NuGet dependencies into the packed nuspec
- Per-target-framework dependencies from plain `PackageReference` items (plus promoted direct deps from bundled packages)
- No `lib/` output (`IncludeBuildOutput=false` by default)
- Works with class libraries, console apps, and Azure Functions source projects

## Installation

Add a development dependency to the project you want to pack:

```xml
<PackageReference Include="Devlead.SourcePack" PrivateAssets="all" />
```

## Quick start

```xml
<PropertyGroup>
  <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
  <SourcePackRoot>Devlead/MyPackage</SourcePackRoot>
  <IsPackable>true</IsPackable>
</PropertyGroup>

<PackageReference Include="Devlead.SourcePack" PrivateAssets="all" />

<SourcePackFile Include="MyPackage.props" Kind="Build" />
<SourcePackFile Include="MyPackage.targets" Kind="Build" />
<SourcePackFile Include="../../README.md" Kind="Metadata" />
```

Pack in Release:

```bash
dotnet pack -c Release
```

For multi-target projects (`TargetFrameworks`), pack single-threaded to avoid duplicate `contentFiles` entries:

```bash
dotnet pack -c Release -- /m:1
```

`Devlead.SourcePack` sets `BuildInParallel=false` as an additional safeguard.

## Properties

| Property                           | Default                         | Description                                        |
|------------------------------------|---------------------------------|----------------------------------------------------|
| `EnableSourcePack`                 | `true`                          | Opt out by setting `false`                         |
| `SourcePackRoot`                   | *(required for auto sources)*   | Root folder under `contentFiles/cs/{tfm}/`         |
| `SourcePackPackConfigurations`     | `Release`                       | Configurations that pack sources                   |
| `SourcePackIncludeGeneratedUsings` | `true`                          | Pack `*.GlobalUsings.g.cs` from `obj/`             |
| `SourcePackFlowBuildAssets`        | `true`                          | Before pack, sets `suppressParent=None` on packed dependencies so the nuspec does not get `exclude="Build,Analyzers"`. Set `false` to keep SDK pack defaults. |

## SourcePackBundle

Re-export source and build assets from a restored dependency package into your nupkg (meta-packages / vendoring), and promote that package's **direct** NuGet dependencies into your packed nuspec.

Declare a matching `PackageReference` so NuGet can restore the package (version via CPM `PackageVersion` or `Version` / `VersionOverride`). When a `SourcePackBundle` matches that id, SourcePack applies `PrivateAssets="all"`, `GeneratePathProperty="true"`, and `Pack="false"` if those are unset:

```xml
<!-- CPM: PackageVersion Include="Devlead.Console" in Directory.Packages.props -->
<PackageReference Include="Devlead.Console" />

<SourcePackBundle Include="Devlead.Console"
                  PackagePathPrefix="Devlead/Advanced" />
```

By default, the bundled package's **direct** NuGet dependencies are promoted into this package's nuspec (for example `Spectre.Console.Cli` and `Microsoft.Extensions.Logging.Console` when bundling `Devlead.Console`). The bundled package id itself is not listed as a dependency (`Pack="false"`). Set `PromoteDependencies="false"` to skip promotion.

| Metadata              | Default   | Description                                                                         |
|-----------------------|-----------|-------------------------------------------------------------------------------------|
| `PackagePathPrefix`   | *(empty)* | Prefix under `contentFiles/cs/{tfm}/` for bundled `.cs` files                       |
| `IncludeBuildAssets`  | `true`    | Copy `build/{tfm}/*.props` and `*.targets` from the dependency                      |
| `PromoteDependencies` | `true`    | Promote the bundled package's direct NuGet dependencies into this package's nuspec  |
| `PackagePathProperty` | `Pkg{Id}` | Override the `Pkg...` MSBuild property name (rarely needed)                         |

Example output paths when bundling `Devlead.Console` with prefix `Devlead/Advanced`:

- `contentFiles/cs/net8.0/Devlead/Advanced/Devlead/Console/Program.cs`
- `build/net8.0/Devlead.Console.props`

See `src/Devlead.SourcePack.Sample.Advanced` for a full in-repo example.

## SourcePackFile kinds

| Kind       | Package path                                   |
|------------|------------------------------------------------|
| `Source`   | `contentFiles/cs/{tfm}/{SourcePackRoot}/...`   |
| `Build`    | `build/{tfm}/{filename}`                       |
| `Metadata` | package root (README, icon, etc.)              |

## Build

```bash
dotnet run --file cake.cs
```

## Example projects

- [Devlead.Console](https://github.com/devlead/Devlead.Console) (also bundled as an example by `Devlead.SourcePack.Sample.Advanced`)
- [Devlead.Testing.MockHttp](https://github.com/devlead/Devlead.Testing.MockHttp)

## License

MIT
