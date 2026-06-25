# Devlead.SourcePack

Opinionated MSBuild package for authoring **source NuGet packages** using standard SDK `dotnet pack`.

## Features

- Ships `.cs` files under `contentFiles/cs/{tfm}/...` with `BuildAction=Compile`
- Optional `build/{tfm}/*.props` and `*.targets` via `SourcePackFile`
- Optional `SourcePackBundle` for meta-packages that re-export dependency source/build assets
- Per-target-framework dependencies from plain `PackageReference` items
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

## SourcePackBundle

Re-export source and build assets from a restored dependency package into your nupkg (meta-packages / vendoring).

Add a matching `PackageReference` (with `GeneratePathProperty="true"` recommended). Bundle paths are resolved from `project.assets.json`, so central package management works without `Version` on the reference:

```xml
<PackageReference Include="Devlead.SourcePack.Sample"
                 PrivateAssets="all"
                 GeneratePathProperty="true"
                 Pack="false" />

<SourcePackBundle Include="Devlead.SourcePack.Sample"
                  PackagePathPrefix="Devlead/Bundled" />
```

| Metadata              | Default | Description                                                       |
|-----------------------|---------|-------------------------------------------------------------------|
| `PackagePathPrefix`   | *(empty)* | Prefix under `contentFiles/cs/{tfm}/` for bundled `.cs` files |
| `IncludeBuildAssets`  | `true`  | Copy `build/{tfm}/*.props` and `*.targets` from the dependency    |
| `PackagePathProperty` | `Pkg{Id}` | Override the `Pkg...` MSBuild property name (rarely needed)   |

Example output paths when bundling `Devlead.SourcePack.Sample` with prefix `Devlead/Bundled`:

- `contentFiles/cs/net8.0/Devlead/Bundled/Devlead/Sample/SampleService.cs`
- `build/net8.0/Devlead.SourcePack.Sample.props`

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

- [Devlead.Console](https://github.com/devlead/Devlead.Console)
- [Devlead.Testing.MockHttp](https://github.com/devlead/Devlead.Testing.MockHttp)

## License

MIT
