# Contributing to EricksonLopez.Caching

Thank you for your interest in contributing to **`EricksonLopez.Caching`**! We welcome bug reports, documentation enhancements, feature proposals, and pull requests.

As a foundational, high-performance Tier 0 infrastructure library, all contributions must respect our architectural invariants, zero-allocation philosophy, and strict quality standards.

---

## Code of Conduct

All contributors and maintainers are expected to uphold the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). Please report unacceptable behavior to [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com).

---

## Architectural Invariants & Rules

Before opening a pull request, ensure your proposed changes adhere to the following rules:

1. **Railway-Oriented Result Paradigm**:
   - All cache methods return `Task<Result<T>>` or `Task<Result<bool>>`.
   - Never throw exceptions for cache misses, network timeouts, or transient backend unavailability. Return explicit `Error.Failure` or appropriate result values.
2. **Native AOT & Trimming Compatibility**:
   - `IsAotCompatible=true` and `EnableTrimAnalyzer=true` are active across all libraries under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
   - Zero reflection, runtime code generation, or dynamic IL emission.
3. **One Type Per File**:
   - Every top-level class, interface, record, struct, or enum must reside in its own dedicated file matching the type name.
4. **License Header**:
   - Every `.cs` source file must begin with the exact copyright banner:
     ```csharp
     // Copyright © Erickson Lopez. MIT License.
     ```
5. **XML Documentation (CS1591)**:
   - All public types, constructors, methods, properties, and parameters must be documented with comprehensive XML doc comments (`<summary>`, `<param>`, `<returns>`, `<exception>`).
   - Warning `CS1591` is strictly treated as an error and must never be suppressed.
6. **No Obsolete APIs**:
   - Usage of `[Obsolete]` is prohibited across the repository.
7. **Zero-Allocation Logging**:
   - Use compile-time `[LoggerMessage]` source generation for all logging definitions.

---

## Development Workflow

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (multi-targeting .NET 8.0, 9.0, and 10.0).
- Git.
- Optional: Docker (for testing against a local Redis instance).

### Local Setup
```bash
git clone https://github.com/ericksonlopezf/dotnet-caching.git
cd dotnet-caching
dotnet restore
dotnet build
dotnet test
```

### Code Formatting
Ensure all code conforms to the repository's `.editorconfig` rules:
```bash
dotnet format --verify-no-changes
```

### Branching Model
- `main`: Protected trunk containing production releases.
- Feature branches: Use descriptive kebab-case naming, e.g., `feature/redis-distributed-lock`, `fix/empty-prefix-guard`.

---

## Pull Request Checklist

When submitting a pull request, ensure:

- [ ] All unit tests pass locally across .NET 8.0, 9.0, and 10.0 (`dotnet test`).
- [ ] Code formatting complies with `.editorconfig` (`dotnet format --verify-no-changes`).
- [ ] No compiler warnings or trimmer warnings exist (`dotnet build -c Release`).
- [ ] All public APIs have XML documentation comments.
- [ ] Every `.cs` file has the `// Copyright © Erickson Lopez. MIT License.` header.
- [ ] New features include comprehensive unit tests and Stryker mutation coverage.
- [ ] Any documentation changes use **kebab-case** filenames and technical English.

---

## Contact & Questions

For questions or design discussions, open a [GitHub Issue](https://github.com/ericksonlopezf/dotnet-caching/issues) or reach out directly to the maintainer at [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com).
