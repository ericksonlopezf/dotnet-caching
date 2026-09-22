# Security Policy

## Supported Versions

The following table outlines the lifecycle and security update policy for `EricksonLopez.Caching`:

| Version | Target Frameworks | Supported | Security Maintenance Level |
| :--- | :--- | :---: | :--- |
| **1.0.x** | `.net8.0`, `.net9.0`, `.net10.0` | :white_check_mark: | Full Active & Security Support |
| < 1.0.0 | Pre-release | :x: | Unsupported |

---

## Reporting a Vulnerability

We treat the security of our libraries with the highest priority. If you identify a potential security vulnerability, memory safety issue, or sensitive data leak in `EricksonLopez.Caching` or `EricksonLopez.Caching.Redis`, please follow this responsible disclosure procedure:

1. **Do NOT open a public GitHub issue or discussion** regarding the vulnerability.
2. Send an email directly to the project maintainer:
   - **Contact**: Erickson Lopez
   - **Email**: [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)
   - **Subject Line**: `[SECURITY VULNERABILITY] EricksonLopez.Caching - <Brief Description>`
3. Include detailed information to assist in reproducing and assessing the vulnerability:
   - Affected package name and version.
   - Minimal reproduction code or test scenario.
   - Potential attack vectors, preconditions, or impact analysis (e.g., cross-tenant cache pollution, denial-of-service via stampede).
   - Any proposed remediation or patches.

### Response Timeline

- **Initial Acknowledgment**: Within 48 hours of receipt.
- **Triage & Assessment**: Within 5 business days, including confirmation of severity and potential CVSS score.
- **Remediation & Patch Release**: Dependent on complexity; critical vulnerabilities will be addressed via hotfix releases on NuGet within 14 business days.
- **Public Disclosure**: Coordinated public disclosure after patched releases have been published to NuGet.org.

---

## Security Invariants

`EricksonLopez.Caching` enforces several security guarantees by design:
1. **Empty Prefix Protection**: Eviction by prefix (`RemoveByPrefixAsync`, `InvalidateByPrefixAsync`) strictly validates against `null`, empty, or whitespace-only inputs (`ArgumentException.ThrowIfNullOrWhiteSpace`), preventing unintended wildcard purges across multi-tenant keyspaces.
2. **Deterministic Keyspace Scoping**: Designed for multitenant architectures, ensuring tenant boundaries cannot collide or leak across tenant partitions.
3. **Atomic Cryptographic Locking**: Distributed stampede locks in Redis utilize high-entropy cryptographic lease tokens and atomic Lua script releases, preventing unauthorized or out-of-order lock releases across distributed nodes.
4. **Native AOT & Memory Safety**: Zero reflection or dynamic code generation in the core library, ensuring strict compile-time verification and trimming safety.
