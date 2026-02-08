# Dev Blog: Fixing the MonoGame Compute Fork for SadConsole 10.8.0

**Date:** 2026-02-08
**Branch:** `philipbawn/fix-build`
**Result:** SadConsole.Examples.Demo.CSharp now builds and runs on Linux without Wine

---

## The Problem

Running `dotnet run` on the SadConsole demo project produced:

```
Unhandled exception. System.IO.FileNotFoundException:
Could not load file or assembly 'MonoGame.Framework, Version=3.8.4.1'
```

**Root cause:** A version mismatch between two dependencies:

| Package | Version | Provides |
|---------|---------|----------|
| `SadConsole.Host.MonoGame` | 10.8.0 | Expects `MonoGame.Framework` **3.8.4.1** |
| `MonoGame.Framework.Compute.DesktopGL` | 3.8.3 | Provides `MonoGame.Framework` **3.8.3** |

The "Compute" variant of MonoGame (by [cpt-max](https://github.com/cpt-max/MonoGame)) adds compute, tessellation, and geometry shader support plus **ShaderConductor** (which eliminates the Wine requirement for shader compilation on Linux). However, cpt-max hadn't updated their fork past 3.8.3, while SadConsole 10.8.0 upgraded to require MonoGame 3.8.4.1.

The standard `MonoGame.Framework.DesktopGL` 3.8.4.1 package exists but requires Wine on Linux for shader compilation -- which is why the Compute variant was chosen in the first place.

---

## The Solution

### Phase 1: Fork and Update the Compute Variant

1. **Forked** `cpt-max/MonoGame` to `philipbawn/MonoGame` using `gh repo fork`
2. **Cloned** to `/home/philip/git/MonoGame`
3. **Added official MonoGame remote** and fetched tag `v3.8.4.1`
4. **Investigated the version gap:**
   - Merge base at commit `9d415cc` (shared ancestor)
   - 329 commits from merge base to v3.8.4.1 (official improvements)
   - ~90 non-merge commits on compute_shader branch (compute features)

5. **Merged `v3.8.4.1` into `compute_shader`** -- produced **53 merge conflicts**

### Phase 2: Resolving 53 Merge Conflicts

Conflicts were categorized and resolved strategically:

| Category | Count | Strategy | Rationale |
|----------|-------|----------|-----------|
| Binary `.mgfxo` shaders | 7 | Keep ours | Pre-compiled with ShaderConductor |
| Test `.fx` shaders | 15 | Keep ours | Written in ShaderConductor syntax |
| Effect resources (`.fxh`, `.fx`, `.bat`) | 4 | Keep ours | ShaderConductor syntax |
| README | 1 | Keep ours | Contains compute fork info |
| Deleted files (modify/delete) | 3 | Keep deleted | `TextureCollection` replaced by `ShaderResourceCollection` |
| Core graphics C# files | 19 | Keep ours | Compute shader architecture rewrites |
| Manually merged | 4 | Both sides | Needed additions from both branches |

**The 4 manually merged files:**

- **`Effect.cs`** -- Added v3.8.4.1's XML documentation comments (ours was empty at the conflict point)
- **`Shader.cs`** -- Kept compute's `size` field AND v3.8.4.1's `ToShaderSemantic()` method
- **`SurfaceFormat.cs`** -- Kept compute's integer surface formats (R32Uint, etc.) AND v3.8.4.1's `Astc4X4Rgba` format
- **`EffectProcessor.cs`** -- Combined compute's `OpenGLES` platform case with v3.8.4.1's `Vulkan`/`GDK` cases; used v3.8.4.1's cleaner `foreach` loop

6. **Updated `MonoGame.props`** version from `3.8.3.1` to `3.8.4.1`
7. **Pushed** to `philipbawn/MonoGame` on GitHub

### Phase 3: Local Project Reference

Instead of building NuGet packages from the fork, we referenced the MonoGame framework project directly:

**Changes to `Examples.csproj`:**

```xml
<!-- BEFORE: NuGet package reference (version mismatch) -->
<PackageReference Include="MonoGame.Framework.Compute.DesktopGL" Version="3.8.3" />

<!-- AFTER: Local project reference to our fork -->
<ProjectReference Include="$(HOME)/git/MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj" />
```

Also added the `MonoGamePlatform` property that the NuGet package used to provide:
```xml
<MonoGamePlatform Condition=" '$(GameHost)' == 'monogame' ">DesktopGL</MonoGamePlatform>
```

The content builder task stayed as a NuGet reference (`MonoGame.Content.Builder.Task.Compute` 3.8.3) since the Effect Compiler tool had additional merge issues (Vulkan shader profile API mismatch) that didn't affect the framework itself.

### Phase 4: Build Fixes

Two post-merge compilation issues were found and fixed:

1. **`Texture3D.cs:92`** -- `PlatformSetData` was called with 13 args but platform implementations only accept 10. Removed redundant `width`, `height`, `depth` parameters (the platform code computes these internally from the bounds).

2. **Git submodules** -- `ThirdParty/StbImageSharp` and `ThirdParty/StbImageWriteSharp` needed `git submodule update --init --recursive`.

---

## Final State

```
Examples.csproj references:
  - SadConsole.Host.MonoGame 10.8.0          (NuGet)
  - SadConsole.Extended 10.8.0               (NuGet)
  - MonoGame.Framework.DesktopGL             (local project: ~/git/MonoGame)
  - MonoGame.Content.Builder.Task.Compute    (NuGet, 3.8.3)
```

The app builds and runs on Linux without Wine. The local MonoGame framework provides the correct assembly version (3.8.4.1) that SadConsole expects, while retaining all compute/tessellation/geometry shader support and ShaderConductor.

---

## Key Files Modified

| File | Change |
|------|--------|
| `Examples.csproj` | Switched from NuGet Compute package to local project reference |
| `~/git/MonoGame/` | Forked compute_shader branch merged with v3.8.4.1 |
| `~/git/MonoGame/MonoGame.props` | Version bumped to 3.8.4.1 |
| `~/git/MonoGame/MonoGame.Framework/Graphics/Texture3D.cs` | Fixed PlatformSetData call signature |

## GitHub References

- **Fork:** https://github.com/philipbawn/MonoGame (branch: `compute_shader`)
- **Upstream (compute):** https://github.com/cpt-max/MonoGame
- **Official MonoGame:** https://github.com/MonoGame/MonoGame (tag: `v3.8.4.1`)

---

## Lessons Learned

1. **Version pinning matters:** When a transitive dependency upgrades (MonoGame 3.8.3 -> 3.8.4.1 via SadConsole), all forks/variants must keep pace.

2. **Merge strategy for heavily-forked repos:** When one branch has deep architectural changes (ShaderResourceCollection replacing TextureCollection, ShaderConductor replacing MojoShader), keeping the fork's version for conflicting core files and selectively pulling in upstream improvements is usually the right call.

3. **Local project references as a bridge:** When a NuGet package is outdated but the fork is updated, a local `<ProjectReference>` sidesteps the NuGet packaging step entirely. The tradeoff is that `$(HOME)/git/MonoGame` must exist with submodules initialized.

4. **Submodules are easy to forget:** MonoGame embeds StbImageSharp and other libraries as git submodules. A shallow clone plus `--init --recursive` is required.

5. **NuGet packages set MSBuild properties:** The `MonoGame.Framework.Compute.DesktopGL` NuGet package set `MonoGamePlatform=DesktopGL` via a `.targets` file. When switching to a project reference, that property must be set manually.
