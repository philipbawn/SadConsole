# Dev Blog Retrospective: Updating the MonoGame Compute Fork for SadConsole 10.8.0 on Linux

**Date:** 2026-02-08
**Author:** Philip Bawn (with Claude Code / Opus 4.6)
**Branch:** `philipbawn/fix-build`
**Result:** SadConsole.Examples.Demo.CSharp now builds and runs on Linux without Wine
**Time Spent:** ~45 minutes of iterative problem-solving

---

## Table of Contents

1. [Background & Context](#background--context)
2. [The Error](#the-error)
3. [Diagnosis: Why This Happened](#diagnosis-why-this-happened)
4. [Why We Couldn't Just Switch Packages](#why-we-couldnt-just-switch-packages)
5. [Phase 1: Forking the Compute Variant](#phase-1-forking-the-compute-variant)
6. [Phase 2: The Merge -- 53 Conflicts](#phase-2-the-merge----53-conflicts)
7. [Phase 3: Conflict Resolution Strategy](#phase-3-conflict-resolution-strategy)
8. [Phase 4: Wiring Up the Local Project Reference](#phase-4-wiring-up-the-local-project-reference)
9. [Phase 5: Build Errors and Post-Merge Fixes](#phase-5-build-errors-and-post-merge-fixes)
10. [Phase 6: The MonoGamePlatform Property Gotcha](#phase-6-the-monogameplatform-property-gotcha)
11. [Final Result](#final-result)
12. [Complete Diff Summary](#complete-diff-summary)
13. [Reproduction Steps](#reproduction-steps)
14. [Future Maintenance Notes](#future-maintenance-notes)
15. [Lessons Learned](#lessons-learned)

---

## Background & Context

The SadConsole Examples Demo is a showcase application for [SadConsole](https://github.com/Thraka/SadConsole), a .NET ASCII/ANSI console engine. It can run on multiple backends: MonoGame, SFML, or FNA. On this system (Pop!_OS Linux), we were using the MonoGame backend.

MonoGame has a standard NuGet package (`MonoGame.Framework.DesktopGL`) but also a community fork by [cpt-max](https://github.com/cpt-max/MonoGame) that adds several important features:

- **Compute shader support** -- GPU general-purpose computing
- **Tessellation shader support** -- Hull and domain shaders for dynamic mesh subdivision
- **Geometry shader support** -- Per-primitive GPU processing
- **ShaderConductor integration** -- Replaces MojoShader for HLSL-to-GLSL cross-compilation, which critically **eliminates the Wine dependency on Linux** for shader compilation

That last point is the key reason we were using the Compute variant: the standard MonoGame Content Builder (MGFXC) requires Wine on Linux to compile `.fx` shader files. ShaderConductor runs natively.

The Compute variant is published as two NuGet packages:
- `MonoGame.Framework.Compute.DesktopGL` -- the runtime framework
- `MonoGame.Content.Builder.Task.Compute` -- the shader build tooling

---

## The Error

Running the demo project produced a wall of nullable warnings (cosmetic, not blocking) followed by a fatal runtime error:

```
Unhandled exception. System.IO.FileNotFoundException:
Could not load file or assembly 'MonoGame.Framework, Version=3.8.4.1,
Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.

   at SadConsole.Game.Create(Builder configuration)
   at SadConsole.Configuration.ExtensionsHost.Run(Builder configBuilder)
   at Program.<Main>$(String[] args) in Program.cs:line 40
```

The application compiled fine but crashed at startup because the .NET runtime couldn't find a MonoGame assembly with version 3.8.4.1.

---

## Diagnosis: Why This Happened

Using the NuGet MCP tools, we investigated the package versions:

| Package | Installed Version | Latest Available | Published |
|---------|-------------------|------------------|-----------|
| `SadConsole.Host.MonoGame` | 10.8.0 | 10.8.0 | Jan 19, 2026 |
| `MonoGame.Framework.Compute.DesktopGL` | 3.8.3 | **3.8.3** (no newer) | Mar 30, 2024 |
| `MonoGame.Framework.DesktopGL` (standard) | -- | **3.8.4.1** | Oct 20, 2025 |
| `MonoGame.Content.Builder.Task.Compute` | 3.8.3 | **3.8.3** (no newer) | Mar 30, 2024 |

The timeline tells the story:
- **March 2024:** cpt-max publishes Compute variants at version 3.8.3
- **October 2025:** Official MonoGame releases 3.8.4.1
- **January 2026:** SadConsole 10.8.0 releases, compiled against MonoGame 3.8.4.1

SadConsole.Host.MonoGame 10.8.0 has a hard dependency on `MonoGame.Framework` assembly version 3.8.4.1. The Compute variant provides version 3.8.3. The .NET assembly loader performs exact version matching and throws `FileNotFoundException` when the versions don't align.

The Compute variant was last updated almost **two years** before SadConsole 10.8.0 released. cpt-max simply hadn't rebased their fork onto the newer MonoGame.

---

## Why We Couldn't Just Switch Packages

The obvious fix would be to replace `MonoGame.Framework.Compute.DesktopGL` with the standard `MonoGame.Framework.DesktopGL` 3.8.4.1. However, this would reintroduce the Wine dependency:

```
MGFXC (MonoGame shader compiler) requires Wine on Linux to compile
the two .fx shader files (FinalDraw.fx and crt-lottes-mg.fx).
Wine is not installed on your system.
```

This was actually documented in the git history (commit `3e9cc424`). The project has two shader files that need compilation, and the standard MonoGame build tooling shells out to MGFXC which uses MojoShader (a Windows-only DLL) via Wine. The Compute variant's ShaderConductor runs natively.

So we needed the Compute fork, but updated to provide the 3.8.4.1 assembly version.

---

## Phase 1: Forking the Compute Variant

### Step 1: Fork on GitHub

```bash
gh repo fork cpt-max/MonoGame --clone=false
```

This created `philipbawn/MonoGame` as a fork of `cpt-max/MonoGame` (itself a fork of `MonoGame/MonoGame`).

### Step 2: Clone locally

```bash
cd /home/philip/git
gh repo clone philipbawn/MonoGame -- --depth=1
```

We initially used a shallow clone for speed, then unshallowed later when we needed the full history for merging.

### Step 3: Set up remotes

The `gh repo clone` automatically set up:
- `origin` -> `philipbawn/MonoGame` (our fork)
- `upstream` -> `cpt-max/MonoGame` (the compute variant)

We manually added the official MonoGame:
```bash
git remote add monogame https://github.com/MonoGame/MonoGame.git
git fetch monogame --tags
```

This gave us access to the `v3.8.4.1` tag.

### Step 4: Investigate the version gap

```bash
# Find common ancestor
git merge-base compute_shader v3.8.4.1
# Result: 9d415cc14bd5

# Count divergence
git log --oneline 9d415cc14bd5..v3.8.4.1 | wc -l    # 329 commits
git log --oneline --no-merges 9d415cc14bd5..compute_shader  # ~90 commits
```

The merge base was commit `9d415cc` -- a point where cpt-max had last synced with official MonoGame's `develop` branch. From there:
- **Official MonoGame** had 329 commits of improvements, bug fixes, XML documentation, new platform support (Vulkan, GDK/Xbox), ASTC texture compression, and general modernization.
- **cpt-max's compute_shader** had ~90 non-merge commits adding compute/tessellation/geometry shaders, ShaderConductor integration, ShaderResourceCollection (replacing TextureCollection), BufferResource base class, integer surface formats, structured buffers, indirect draw, and numerous shader compilation fixes.

---

## Phase 2: The Merge -- 53 Conflicts

We chose a merge strategy (rather than rebase) because cpt-max had historically been merging upstream changes:

```bash
git merge v3.8.4.1 --no-edit
```

This produced **53 merge conflicts** across these categories:

### Conflict Inventory

| File Type | Count | Files |
|-----------|-------|-------|
| Binary `.mgfxo` (precompiled shaders) | 7 | AlphaTestEffect, BasicEffect, EnvironmentMapEffect, SkinnedEffect (dx11 + ogl variants) |
| Test `.fx` shaders | 15 | Bevels, BlackOut, ColorFlip, CustomSpriteBatchEffect, Grayscale, HighContrast, Instancing, Invert, NoEffect, ParserTest, PreprocessorTest, RainbowH, TextureArrayEffect, VertexTextureEffect, etc. |
| Effect resources (`.fxh`, `.fx`, `.bat`) | 4 | Lighting.fxh, Structures.fxh, SkinnedEffect.fx, RebuildMGFX.bat |
| README | 1 | README.md |
| Modify/delete conflicts | 3 | TextureCollection.cs, TextureCollection.DirectX.cs, IndexBuffer.DirectX.cs |
| Core framework C# | 15 | GraphicsDevice.cs, SamplerStateCollection.cs, Shader.cs, ShaderStage.cs, Texture.cs, Texture2D.cs, Texture3D.cs, SurfaceFormat.cs, Effect.cs, EffectParameter.cs, DynamicIndexBuffer.cs, DynamicVertexBuffer.cs, IndexBuffer.cs, VertexBuffer.cs, etc. |
| Platform-specific C# | 4 | GraphicsDevice.DirectX.cs, SamplerStateCollection.DirectX.cs, ShaderProgramCache.cs, DirectX.targets |
| Tools (Effect Compiler) | 4 | EffectProcessor.cs, OutputParser.cs, EffectObject.writer.cs, MonoGame.Effect.Compiler.csproj, Program.cs |

### Understanding the Nature of Conflicts

The conflicts fell into distinct patterns:

**Pattern 1: ShaderConductor vs MojoShader syntax** (test .fx files, effect resources)
The compute branch rewrote shaders to use explicit `sampler`/`Texture2D` with `register()` semantics (ShaderConductor style), while official MonoGame uses `DECLARE_TEXTURE`/`SAMPLE_TEXTURE` macros (MojoShader style). These are fundamentally different approaches to the same shaders.

**Pattern 2: TextureCollection removal** (modify/delete conflicts)
The compute branch deleted `TextureCollection.cs` and `TextureCollection.DirectX.cs` entirely, replacing them with a new `ShaderResourceCollection` class that supports all shader stages (not just pixel and vertex). Official MonoGame 3.8.4.1 modified these files (adding docs, fixing bugs), creating modify/delete conflicts.

**Pattern 3: XML documentation additions** (most C# files)
Official MonoGame 3.8.4.1 added extensive XML documentation (`/// <summary>`, `/// <param>`, etc.) to many classes. The compute branch didn't have these docs. In most cases, the actual code underneath was different too (compute's architectural changes), making automatic merge impossible.

**Pattern 4: Architectural divergence** (GraphicsDevice, SamplerStateCollection, buffers)
The compute branch fundamentally restructured how shader resources are managed:
- `IndexBuffer` and `VertexBuffer` inherit from `BufferResource` (not `GraphicsResource`)
- `SamplerStateCollection` constructor no longer takes a `ShaderStage` parameter
- `GraphicsDevice` creates `ShaderResourceCollection` for 6 shader stages instead of `TextureCollection` for 2

**Pattern 5: New platform targets** (EffectProcessor.cs)
Official MonoGame 3.8.4.1 added Vulkan and GDK (Xbox) platform support. The compute branch added OpenGL ES as a separate profile. Both needed to be present.

---

## Phase 3: Conflict Resolution Strategy

### Guiding Principle

> The compute_shader branch's changes ARE the feature. We're building this fork specifically for its compute/tessellation/geometry/ShaderConductor support. When in doubt, keep the compute branch's version and accept that some v3.8.4.1 improvements (mainly XML docs) won't be included.

### Resolution by Category

#### Binary `.mgfxo` files (7 files) -- Keep ours
```bash
for f in $(git diff --name-only --diff-filter=U | grep '\.mgfxo$'); do
  git checkout --ours "$f" && git add "$f"
done
```
These are precompiled shader bytecode files. The compute branch compiled them with ShaderConductor; v3.8.4.1 compiled them with MojoShader. They're incompatible formats. Ours are the correct ones.

#### Test `.fx` shaders (15 files) -- Keep ours
```bash
for f in $(git diff --name-only --diff-filter=U | grep '^Tests/'); do
  git checkout --ours "$f" && git add "$f"
done
```
Example of the difference:
```hlsl
// compute_shader version (ShaderConductor syntax):
sampler s0 : register(s0);
Texture2D tex : register(t0);
float4 color = tex.Sample(s0, coords);

// v3.8.4.1 version (MojoShader macro syntax):
DECLARE_TEXTURE(s, 0);
float4 color = SAMPLE_TEXTURE(s, coords);
```

#### Effect resources (4 files) -- Keep ours
Lighting.fxh, Structures.fxh, SkinnedEffect.fx, and RebuildMGFX.bat all use ShaderConductor-specific syntax and build scripts.

#### README -- Keep ours
Contains compute fork documentation.

#### Modify/delete conflicts (3 files) -- Keep deleted
```bash
git rm -f \
  "MonoGame.Framework/Graphics/TextureCollection.cs" \
  "MonoGame.Framework/Platform/Graphics/TextureCollection.DirectX.cs" \
  "MonoGame.Framework/Platform/Graphics/Vertices/IndexBuffer.DirectX.cs"
```
These files were intentionally deleted by the compute branch:
- `TextureCollection.cs` -> replaced by `ShaderResourceCollection.cs`
- `TextureCollection.DirectX.cs` -> replaced by `ShaderResourceCollection.DirectX.cs`
- `IndexBuffer.DirectX.cs` -> functionality moved to `BufferResource.DirectX.cs`

#### ShaderStage.cs, ShaderProgramCache.cs, SamplerStateCollection.DirectX.cs, DirectX.targets -- Keep ours
These have compute-specific structural changes (6 shader stages, ShaderResourceCollection bindings, etc.) that are core to the fork's functionality. v3.8.4.1's changes to these files are minor.

#### Core framework files (15 files) -- Keep ours
```bash
for f in \
  "MonoGame.Framework/Graphics/GraphicsDevice.cs" \
  "MonoGame.Framework/Graphics/SamplerStateCollection.cs" \
  "MonoGame.Framework/Graphics/Texture.cs" \
  "MonoGame.Framework/Graphics/Texture2D.cs" \
  "MonoGame.Framework/Graphics/Texture3D.cs" \
  ...
do
  git checkout --ours "$f" && git add "$f"
done
```

The rationale: these files have deep architectural changes in the compute branch (BufferResource inheritance, ShaderAccess constructors, ShaderResourceCollection, CopyData methods, etc.). The v3.8.4.1 changes are predominantly XML documentation additions and minor bug fixes that would require careful line-by-line integration with the rewritten code. The risk of introducing subtle bugs far outweighs the benefit of XML docs.

#### Tools (OutputParser.cs, EffectObject.writer.cs, MonoGame.Effect.Compiler.csproj, Program.cs) -- Keep ours
These contain ShaderConductor integration, MojoShader fallback logic, and native library references that are central to the fork's build tooling.

### The 4 Manually Merged Files

These files had conflicts where both sides contributed valuable, non-overlapping changes:

#### 1. `Effect.cs` -- XML docs (1 conflict)
v3.8.4.1 added XML documentation comments for the `Effect(GraphicsDevice, byte[], int, int)` constructor. The compute branch had nothing at that location. Resolution: accept the docs.

```csharp
// v3.8.4.1 added this documentation block:
/// <summary>
/// Creates a new instance of <see cref="Effect"/>.
/// </summary>
/// <param name="graphicsDevice">Graphics device</param>
/// <param name="effectCode">The effect code.</param>
/// <param name="index"></param>
/// <param name="count"></param>
/// <exception cref="ArgumentException">This <paramref name="effectCode"/> is invalid.</exception>
```

#### 2. `Shader.cs` -- Both additions needed (1 conflict)
The compute branch added a `size` field to `ShaderAttributeInfo`. v3.8.4.1 added a `ToShaderSemantic()` method. Both are independent additions that don't conflict semantically:

```csharp
public string name;
public int location;
public int size;               // <-- compute_shader addition

public string ToShaderSemantic()  // <-- v3.8.4.1 addition
{
    switch (usage)
    {
        case VertexElementUsage.Position: return "POSITION" + index;
        case VertexElementUsage.Color: return "COLOR" + index;
        // ... etc
    }
}
```

#### 3. `SurfaceFormat.cs` -- Enum entries from both sides (1 conflict)
Both branches added entries to the `SurfaceFormat` enum after the ETC2 block:

```csharp
// Shared (both branches had these):
Rgb8Etc2 = 90,
Srgb8Etc2 = 91,
Rgb8A1Etc2 = 92,
Srgb8A1Etc2 = 93,
Rgba8Etc2 = 94,
SRgb8A8Etc2 = 95,

// v3.8.4.1 added:
Astc4X4Rgba = 96,

// compute_shader added (after #endregion):
R32Uint, R32Int, R16Uint, R16Int, R8Uint, R8Int,
Rg64Uint, Rg64Int, Rg32Uint, Rg32Int, Rg16Uint, Rg16Int,
Rgba128Uint, Rgba128Int, Rgba64Uint, Rgba64Int, Rgba32Uint, Rgba32Int,
```

Resolution: kept all entries from both sides.

#### 4. `EffectProcessor.cs` -- Platform cases + loop style (2 conflicts)

**Conflict 1: Platform switch cases**
```csharp
// compute_shader added:
case TargetPlatform.iOS:
case TargetPlatform.Android:
    return "OpenGLES";

// v3.8.4.1 added:
case TargetPlatform.DesktopVK:
    return "Vulkan";
case TargetPlatform.WindowsGDK:
case TargetPlatform.XboxOne:
case TargetPlatform.XboxSeries:
    return "GDK";
```
Resolution: included all platform cases.

**Conflict 2: Error processing loop**
Compute used `for (var i = 0; ...)` with index-based access; v3.8.4.1 used `foreach`. The rest of the method used v3.8.4.1's variable name (`errorOrWarningLine`), so we kept the foreach.

---

## Phase 4: Wiring Up the Local Project Reference

### First Attempt: Reference both framework and content builder

```xml
<ProjectReference Include="$(HOME)/git/MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj" />
<ProjectReference Include="$(HOME)/git/MonoGame/Tools/MonoGame.Content.Builder.Task/MonoGame.Content.Builder.Task.csproj" />
```

This failed because the Content Builder Task depends on the Effect Compiler, which pulls in `ShaderProfile.Vulkan.cs` -- a new file from v3.8.4.1 that implements the Vulkan shader profile using v3.8.4.1's API signatures. Since we kept compute_shader's `ShaderProfile` base class (which has different abstract method signatures), the Vulkan profile couldn't compile:

```
error CS0534: 'VulkanShaderProfile' does not implement inherited abstract member
'ShaderProfile.CreateShader(ShaderResult, string, string, ShaderStage, EffectObject, Options, ref string)'
```

The Effect Compiler has its own set of merge issues we'd need to resolve. But the SadConsole project doesn't actually need to build the effect compiler -- it just needs the framework DLL and the shader build tooling.

### Final Approach: Mixed references

```xml
<!-- Framework: local project reference (gets us the 3.8.4.1 assembly) -->
<ProjectReference Include="$(HOME)/git/MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj" />

<!-- Content builder: keep the NuGet package (for shader compilation tooling) -->
<PackageReference Include="MonoGame.Content.Builder.Task.Compute" Version="3.8.3" />
```

This works because:
1. The framework DLL is built from our fork (merged with v3.8.4.1, providing the correct assembly version)
2. The content builder NuGet package at 3.8.3 is used only at build time for shader compilation -- it doesn't contribute a runtime assembly that SadConsole checks

---

## Phase 5: Build Errors and Post-Merge Fixes

### Error 1: Missing StbImageSharp

```
error CS0246: The type or namespace name 'StbImageSharp' could not be found
```

MonoGame embeds third-party libraries as git submodules. Our initial shallow clone didn't include them:

```bash
cd /home/philip/git/MonoGame
git submodule update --init --recursive
```

This cloned:
- `ThirdParty/StbImageSharp` -- STB-based image loading
- `ThirdParty/StbImageWriteSharp` -- STB-based image writing
- `ThirdParty/Dependencies` -- native library binaries
- `ThirdParty/SDL_GameControllerDB` -- game controller mappings
- `external/MonoGame.Templates` -- project templates
- `native/monogame/external/sdl2` -- SDL2 (with its own sub-submodules)
- `native/monogame/external/vma` -- Vulkan Memory Allocator
- `native/monogame/external/volk` -- Vulkan loader
- `native/monogame/external/vulkan-headers` -- Vulkan API headers

### Error 2: Texture3D.PlatformSetData signature mismatch

```
error CS1501: No overload for method 'PlatformSetData' takes 13 arguments
```

In `Texture3D.cs` line 92, the caller was passing 13 arguments:
```csharp
var width = right - left;
var height = bottom - top;
var depth = back - front;
PlatformSetData(level, left, top, right, bottom, front, back,
                data, startIndex, elementCount, width, height, depth);
```

But the platform implementations (`Texture3D.OpenGL.cs`, `Texture3D.DirectX.cs`) only accept 10 parameters and compute `width`, `height`, `depth` internally:

```csharp
private void PlatformSetData<T>(
    int level, int left, int top, int right, int bottom, int front, int back,
    T[] data, int startIndex, int elementCount)
{
    var width = right - left;   // computed internally
    var height = bottom - top;
    var depth = back - front;
    ...
}
```

This was a merge artifact -- the compute_shader branch's `Texture3D.cs` had been merged from an intermediate upstream state where the signature was being changed. The fix was simple: remove the redundant arguments and the local variable computation:

```csharp
// Fixed:
PlatformSetData(level, left, top, right, bottom, front, back,
                data, startIndex, elementCount);
```

---

## Phase 6: The MonoGamePlatform Property Gotcha

After fixing the compilation errors, a build-time error appeared:

```
error: The MonoGamePlatform property was not defined in the project!
```

This came from `MonoGame.Content.Builder.Task.Compute.targets` line 108, which requires knowing which platform to target for shader compilation.

Investigation revealed that the NuGet package `MonoGame.Framework.Compute.DesktopGL` included a `.targets` file that set this property:

```xml
<!-- MonoGame.Framework.Compute.DesktopGL.targets (inside NuGet package) -->
<Project>
  <PropertyGroup>
    <MonoGamePlatform>DesktopGL</MonoGamePlatform>
  </PropertyGroup>
</Project>
```

Since we replaced the NuGet reference with a local `<ProjectReference>`, this `.targets` file was no longer being imported. The fix was to set the property manually in the project's `<PropertyGroup>`:

```xml
<PropertyGroup>
    <GameHost>monogame</GameHost>
    <MonoGamePlatform Condition=" '$(GameHost)' == 'monogame' ">DesktopGL</MonoGamePlatform>
</PropertyGroup>
```

After this, `dotnet build` succeeded and `dotnet run` launched the SadConsole demo window.

---

## Final Result

### Examples.csproj (final state of the MonoGame section)

```xml
<ItemGroup Condition=" '$(GameHost)' == 'monogame' ">
    <!-- Local project reference to MonoGame compute_shader fork (merged with v3.8.4.1) -->
    <ProjectReference Include="$(HOME)/git/MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj" />

    <!-- Compile the MonoGame Shaders (uses ShaderConductor - no Wine needed on Linux) -->
    <PackageReference Include="MonoGame.Content.Builder.Task.Compute" Version="3.8.3" />
    <MonoGameContentReference Include="Content\Assets.mgcb" />
    <None Remove="Content\bin\**\*" />
    <None Remove="Content\obj\**\*" />
</ItemGroup>
```

### Dependency Graph

```
Examples.csproj
  |
  +-- SadConsole.Host.MonoGame 10.8.0 (NuGet)
  |     +-- expects MonoGame.Framework 3.8.4.1 at runtime
  |
  +-- SadConsole.Extended 10.8.0 (NuGet)
  |
  +-- MonoGame.Framework.DesktopGL (LOCAL PROJECT)
  |     +-- Built from philipbawn/MonoGame, branch compute_shader
  |     +-- Merged with official v3.8.4.1
  |     +-- Provides MonoGame.Framework assembly (version 3.8.4.1)
  |     +-- Includes compute/tessellation/geometry shader support
  |     +-- Uses ShaderConductor (no Wine needed)
  |
  +-- MonoGame.Content.Builder.Task.Compute 3.8.3 (NuGet)
        +-- Build-time only (shader compilation)
        +-- Uses ShaderConductor for .fx -> .mgfxo
```

---

## Complete Diff Summary

### Files modified in SadConsole repo

| File | Change |
|------|--------|
| `Examples.csproj` | Replaced `MonoGame.Framework.Compute.DesktopGL` NuGet reference with local `ProjectReference`. Added `MonoGamePlatform` property. |

### Files modified in MonoGame fork (philipbawn/MonoGame)

| File | Change |
|------|--------|
| `MonoGame.props` | Version `3.8.3.1` -> `3.8.4.1` |
| `MonoGame.Framework/Graphics/Texture3D.cs` | Removed extra `width, height, depth` args from `PlatformSetData` call |
| `MonoGame.Framework/Graphics/Effect/Effect.cs` | Added v3.8.4.1 XML docs |
| `MonoGame.Framework/Graphics/Shader/Shader.cs` | Added v3.8.4.1's `ToShaderSemantic()` method alongside compute's `size` field |
| `MonoGame.Framework/Graphics/SurfaceFormat.cs` | Added v3.8.4.1's `Astc4X4Rgba` format alongside compute's integer formats |
| `MonoGame.Framework.Content.Pipeline/Processors/EffectProcessor.cs` | Added Vulkan/GDK platform cases alongside compute's OpenGLES |
| + 329 auto-merged commits from v3.8.4.1 | Bug fixes, improvements, new platform support |

### Commits on the fork

```
b7bd272 Update version to 3.8.4.1 to match official MonoGame release
06de4fd Merge tag 'v3.8.4.1' into compute_shader
```

---

## Reproduction Steps

To recreate this setup from scratch on a new machine:

```bash
# 1. Clone the MonoGame fork
cd ~/git
gh repo clone philipbawn/MonoGame
cd MonoGame
git submodule update --init --recursive

# 2. Clone the SadConsole repo
cd ~/git
git clone <sadconsole-repo-url> SadConsole
cd SadConsole
git checkout philipbawn/fix-build

# 3. Build and run
cd templates/template_code/SadConsole.Examples.Demo.CSharp
dotnet run
```

### Prerequisites
- .NET 10 SDK
- No Wine required (ShaderConductor handles shader cross-compilation natively)
- Git with submodule support

---

## Future Maintenance Notes

### When SadConsole upgrades MonoGame again

If a future SadConsole version requires MonoGame > 3.8.4.1, repeat the merge process:

```bash
cd ~/git/MonoGame
git fetch monogame --tags
git merge v<new-version> --no-edit
# Resolve conflicts (likely similar pattern)
# Update MonoGame.props version
git push origin compute_shader
```

### When cpt-max updates the Compute fork

If cpt-max publishes a new `MonoGame.Framework.Compute.DesktopGL` NuGet package at >= 3.8.4.1, you can switch back to the NuGet reference and remove the local project reference:

```xml
<!-- Switch back to NuGet if/when updated -->
<PackageReference Include="MonoGame.Framework.Compute.DesktopGL" Version="X.Y.Z" />
```

### Content Builder Task

The `MonoGame.Content.Builder.Task.Compute` NuGet is still at 3.8.3. This hasn't caused issues because it's build-time only. If it breaks in the future, the Effect Compiler's `ShaderProfile.Vulkan.cs` will need to be updated to match compute_shader's `ShaderProfile` API.

### Keeping the fork synced

To pull new compute_shader changes from cpt-max:
```bash
cd ~/git/MonoGame
git fetch upstream
git merge upstream/compute_shader
git push origin compute_shader
```

---

## Lessons Learned

### 1. Version pinning in the .NET ecosystem is strict

.NET assembly loading performs exact version matching. When `SadConsole.Host.MonoGame` was compiled against `MonoGame.Framework, Version=3.8.4.1`, it will not accept 3.8.3. There's no "close enough" -- the assembly loader throws `FileNotFoundException`. This makes dependency version alignment critical.

### 2. Community forks create maintenance burden

The Compute variant solves a real problem (no Wine on Linux) but creates a version-tracking obligation. When the upstream moves, someone has to merge. In this case, ~2 years of drift accumulated ~329 commits of divergence.

### 3. Merge strategy matters for architectural forks

When one branch has deep structural changes (new base classes, removed files, renamed abstractions), the safest merge strategy is to keep the fork's version for conflicted files and selectively pull in upstream improvements. Attempting to interleave changes line-by-line risks subtle bugs in the graphics pipeline.

### 4. Local project references are powerful

MSBuild's `<ProjectReference>` can point to any `.csproj` on disk. This lets you bypass NuGet entirely for development, eliminating the pack-publish-restore cycle. The tradeoff is that the source must be present at the expected path. Using `$(HOME)` makes paths portable across users.

### 5. NuGet packages inject MSBuild properties

Packages can ship `.props` and `.targets` files that set properties and define build tasks. When you remove a `<PackageReference>`, you lose those injected properties. In our case, `MonoGamePlatform=DesktopGL` was silently provided by the NuGet package and had to be manually restored.

### 6. Git submodules are invisible until they're not

MonoGame's dependency on `StbImageSharp` via git submodules meant a clone (even unshallowed) wouldn't compile until `git submodule update --init --recursive` was run. This pulled ~12 repositories including SDL2, Vulkan headers, and image processing libraries.

### 7. Build-time vs runtime dependencies can be separated

The critical insight was that we only needed the framework DLL at runtime (version 3.8.4.1), while the shader build tooling could stay at 3.8.3 (NuGet). This let us avoid fixing the Effect Compiler's merge issues while still getting a working build.

---

## GitHub References

- **Our Fork:** https://github.com/philipbawn/MonoGame (branch: `compute_shader`)
- **Upstream Compute Fork:** https://github.com/cpt-max/MonoGame
- **Official MonoGame:** https://github.com/MonoGame/MonoGame (tag: `v3.8.4.1`)
- **SadConsole:** https://github.com/Thraka/SadConsole
- **NuGet - MonoGame.Framework.Compute.DesktopGL:** https://www.nuget.org/packages/MonoGame.Framework.Compute.DesktopGL/
- **NuGet - MonoGame.Framework.DesktopGL:** https://www.nuget.org/packages/MonoGame.Framework.DesktopGL/
