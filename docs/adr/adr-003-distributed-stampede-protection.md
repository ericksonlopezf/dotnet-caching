# ADR-003: Distributed Stampede Protection Strategy

> **Sequence:** ← [ADR-002: Package Existence Justification](./adr-002-package-existence-justification.md) | **Next:** [ADR-004: L1/L2 TTL Separation & Graceful Degradation](./adr-004-l1-l2-ttl-separation.md) →

## Status
Accepted (2026-09-03)

## Date
2026-09-03

## Context
While [ADR-001](./adr-001-cache-architecture.md) established single-flight stampede protection in-process via `SemaphoreSlim`, multi-node deployments utilizing `RedisCacheProvider` require an atomic, cross-instance coordination mechanism. In distributed microservices or multi-instance web apps, simultaneous cache misses on cold keys cause hundreds of concurrent requests to hammer the backend database (the "thundering herd" problem).

To satisfy our architectural invariants ([ADR-002](./adr-002-package-existence-justification.md)), distributed locking must:
1. **Require Zero External Dependencies**: No heavyweight third-party locking libraries (Medallion, RedLock.net, or Polly).
2. **Be Native AOT Compatible**: No dynamic code generation or reflection.
3. **Guarantee Deadlock Immunity**: In the event of an abrupt process crash, locks must automatically expire.
4. **Prevent Accidental Unlock**: A node executing slowly must never release a lock acquired by another node after lease expiration.

## Decision
We implement a distributed single-flight locking mechanism in `RedisCacheProvider.GetOrCreateAsync` using native Redis primitives and an atomic Lua release script.

### 1. Lock Acquisition (`SETNX` with Lease Expiry)
When a cache miss occurs in `GetOrCreateAsync`, the provider generates a unique lock token (`Guid.NewGuid().ToString("N")` — a sufficiently random 128-bit identifier with negligible collision probability for this use case) and attempts to acquire an exclusive lock:
```text
SET {fullKey}:__lock {lockToken} NX PX {LockTimeoutMilliseconds}
```
If the lock is acquired:
- The node re-checks the cache inside the lock (double-checked locking).
- If still missing, the node executes the factory delegate.
- The node updates the cache via `SetAsync`.
- Finally, the node releases the lock using the release script.

### 2. Atomic Token-Validated Unlock (Lua Script)
To avoid releasing another instance's lock if the factory execution exceeded `LockTimeout`, release is performed using an atomic Lua script:
```lua
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
else
    return 0
end
```

### 3. Spin-Wait and Polling
If the lock is held by another instance:
- The calling instance enters a polling loop with interval `LockRetryInterval` (default 50ms) up to `MaxLockWaitTime` (default 30s).
- On each poll, it re-queries `GetAsync`. As soon as the lock holder finishes caching the value, the waiting instance reads it and returns immediately.
- If the wait times out without a cached value, the instance executes the factory as a resilient fallback.

## Alternatives Considered
- **RedLock Multi-Master Algorithm**: Rejected. RedLock requires connecting to 3 or 5 independent Redis masters and adds significant complexity and latency. For cache regeneration (which is idempotent and non-financial), single-master atomic `SETNX` provides optimal throughput and sufficient safety.
- **Probabilistic Early Eviction (XFetch)**: Useful for background pre-fetching of predictable keys, but does not prevent cold-start stampedes when an entirely new key is accessed for the first time.

## Consequences

### Positive
- Prevents database connection exhaustion across Kubernetes pods and multi-instance clusters.
- Zero extra dependencies—implemented exclusively over `StackExchange.Redis`.
- Completely deadlock-free due to mandatory Redis lease expiration (`LockTimeout`).
- Token validation guarantees that delayed operations never inadvertently delete locks owned by other processes.

### Negative
- Cold keys incur an additional Redis round-trip to acquire and release the lock.
- In worst-case lock contention where the lock holder takes several seconds, competing requests experience increased latency while spin-waiting.

---
*Navigation: ← [ADR-002: Package Existence Justification](./adr-002-package-existence-justification.md) | [ADR Index](./README.md) | Next: [ADR-004: L1/L2 TTL Separation & Graceful Degradation](./adr-004-l1-l2-ttl-separation.md) →*
