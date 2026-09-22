# ADR-006: Exclusion of Redis Pub/Sub Backplane from Core Caching Packages

> **Sequence:** ← [ADR-005: Tag-Based Invalidation Strategy](./adr-005-tag-based-invalidation.md) | **End of Sequence**

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
In a multi-node cluster utilizing `HybridCacheProvider` ([ADR-004](./adr-004-l1-l2-ttl-separation.md)), each application node maintains its own local L1 in-memory cache. When Node A updates or invalidates a key (via `RemoveAsync`, `RemoveByPrefixAsync`, or `RemoveByTagAsync`), Node B continues to hold the prior value in its local L1 cache until that L1 entry expires naturally.

Some third-party libraries (e.g., FusionCache, EasyCaching) embed a distributed backplane directly into their core packages, using Redis Pub/Sub to broadcast invalidation messages between application nodes. We evaluated whether to include a Redis Pub/Sub backplane inside `EricksonLopez.Caching` or `EricksonLopez.Caching.Redis`.

## Decision
We deliberately exclude Redis Pub/Sub backplane synchronization from the core caching packages (`EricksonLopez.Caching` and `EricksonLopez.Caching.Redis`).

This decision is governed by the following architectural principles:

1. **Single Responsibility Principle (SRP)**:
   - `EricksonLopez.Caching` is a data storage and retrieval abstraction focused on performance, stampede protection, and Result-pattern error handling.
   - Real-time publish/subscribe event broadcasting is a messaging infrastructure concern with complex operational modes (reconnect handling, buffer overflow, at-least-once delivery, channel multiplexing).
2. **Native AOT Invariant**:
   - Long-lived background subscription loops, thread pool dispatching, and dynamic channel listener management introduce synchronization complexity and potential trimming/AOT pitfalls.
3. **L1 TTL Bounded Drift**:
   - Under [ADR-004](./adr-004-l1-l2-ttl-separation.md) (`HybridCacheEntryOptions`), L1 TTL is intentionally kept short (e.g., 30 to 120 seconds), while L2 retains the authoritative data for hours.
   - For 99% of business domains, an eventual consistency window of 30–60 seconds across cluster nodes is fully acceptable and vastly preferable to maintaining hundreds of persistent Redis Pub/Sub socket subscriptions under high Kubernetes pod churn.
4. **Definitive Rejection (Closed, No Future Plan)**:
   - The Backplane is **permanently rejected** from the EricksonLopez.Caching ecosystem — not deferred, not planned as a future package. Domains requiring sub-second cross-node eviction must adopt a purpose-built event messaging library (e.g., `EricksonLopez.Messaging` or direct Redis Pub/Sub subscriptions). The EricksonLopez.Caching contract covers all scenarios where `LocalCacheDuration` (30–120 seconds) provides acceptable consistency guarantees, which covers 100% of the current EricksonLopez ecosystem consumers.

## Consequences

### Positive
- Keeps `EricksonLopez.Caching` and `EricksonLopez.Caching.Redis` lean, fast, and simple to reason about.
- Zero extra socket overhead or Redis thread pool contention for pub/sub message processing.
- Preserves 100% Native AOT compatibility and zero-allocation characteristics.

### Negative
- Application nodes experience bounded drift for the duration of the L1 local TTL when another node updates data. Consumers needing immediate invalidation must either rely on direct L2 reads or set small `LocalCacheDuration` values.

---
*Navigation: ← [ADR-005: Tag-Based Invalidation Strategy](./adr-005-tag-based-invalidation.md) | [ADR Index](./README.md)*
