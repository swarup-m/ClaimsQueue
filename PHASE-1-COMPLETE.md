# Phase 1: Setup & Schema — COMPLETE ✓

**Completed:** 2026-09-29  
**Time Spent:** ~45 minutes  
**Status:** Ready for Phase 2 (Core APIs)

---

## What Was Done

### 1. Project Structure ✓
- Created ASP.NET Core 10 solution with 2 projects:
  - `src/HealthcareClaimsQueue.API` — Web API
  - `test/HealthcareClaimsQueue.Tests` — Unit tests (xUnit)
- Added NuGet packages:
  - EF Core 10.0.0 (DbContext, migrations)
  - SQL Server provider
  - OpenAPI / Swagger

### 2. Data Models ✓
Created C# models matching the SQL schema exactly:
- `Queue.cs` — 6 queues (DUP, AUTH, CASE, BENE, PEND, OTHER)
- `Claim.cs` — Insurance claim records
- `AppUser.cs` — Supervisors + reviewers
- `UserSession.cs` — Login tracking (read-only)
- `ReviewTask.cs` — **Core model** — claim in a queue

### 3. EF Core Setup ✓
- `QueueDbContext.cs` — Full fluent mapping
- Column naming configured to match SQL schema (snake_case)
- Foreign key relationships defined
- Indexes created for performance
- Default `"Open"` status set on ReviewTask

### 4. Database ✓
- Database created: `HealthcareClaimsQueue` (LocalDB)
- All schema migrated via EF Core
- Seed data applied:
  - 6 queues
  - 8 users (sfranklin + 7 reviewers)
  - 24 claims
  - 31 open review tasks
  - 15 closed review tasks (for metrics)
  - 13 user sessions

### 5. Configuration ✓
- `appsettings.json` configured:
  - Connection string: `(localdb)\mssqllocaldb`
  - `ReviewLockDurationMinutes: 15`
  - `PendTaskDueDateDays: 7`
- `Program.cs` wired up:
  - DbContext registered
  - CORS enabled
  - Auto-migration on startup

---

## Database Summary

```
┌─────────────────────────────────────────┐
│   HealthcareClaimsQueue (LocalDB)      │
├─────────────────────────────────────────┤
│ Tables:                                │
│  • queue              6 rows            │
│  • app_user           8 rows            │
│  • claim             24 rows            │
│  • review_task       46 rows (31 open)  │
│  • user_session      13 rows            │
│                                         │
│ Ready for API endpoints                │
│ Ready for Phase 2 implementation        │
└─────────────────────────────────────────┘
```

---

## Test Scenarios Seeded

The following scenarios are now in the database for testing:

1. **Selection Algorithm Testing**
   - Scenario A: Tie on priority & due date → must break on claim_number
   - Scenario B: Assignment outranks priority
   - Scenario C: Expired lock must be selectable (task 20, CASE queue)
   - Scenario D: Live lock must be skipped (task 21, CASE queue)
   - Scenario E: Cascade closure scenario (Claim 0000419310, 3 queues)
   - Scenario F: Cascade while sibling locked (Claim 0000419602)

2. **Lock Testing**
   - Expired lock: CASE queue, task 20 (locked by mkowalski, expired 25 min ago)
   - Live lock: CASE queue, task 21 (locked by apatel, expires in ~10 min)

3. **Metrics Testing**
   - Completed tasks across 2 days (some inside SLA, some missed)
   - Mix of Approve, PartialDenial, Deny outcomes
   - Cascade closures with NULL closed_by_user_id

---

## Next Steps: Phase 2

You're now ready to implement the 6 API endpoints:

1. **List Review Tasks** (20 min) — Paginated list with sorting
2. **Get Next Claim** (15 min) — Selection algorithm
3. **Review Claim** (25 min) — **Lock acquisition** (hardest, test concurrency)
4. **Release Claim** (15 min) — Release lock
5. **Forward Claim** (15 min) — Reassign task
6. **Complete Review** (25 min) — Close or pend task

**Critical:** Test lock race concurrency in Review Claim endpoint (2 reviewers click simultaneously).

---

## Project Paths

```
F:\ClaimsQueue\
├── src\HealthcareClaimsQueue.API\
│   ├── Program.cs                   ← Updated with EF Core
│   ├── appsettings.json             ← DB connection + config
│   ├── Models\                       ← Domain models
│   ├── Data\
│   │   ├── QueueDbContext.cs        ← EF Core mappings
│   │   └── Migrations\              ← Schema migrations
│   └── Controllers\                 ← Ready for Phase 2
├── test\HealthcareClaimsQueue.Tests\
│   └── (Ready for Phase 4 tests)
├── sql\
│   ├── seed-complete.sql            ← All test data
│   └── seed.sql                     ← Minimal seed
└── PHASE-1-COMPLETE.md              ← This file
```

---

## Key Decisions Confirmed

✓ 15-minute lock duration (configurable in appsettings)
✓ 7-day pend due date (configurable)
✓ "Open" / "Closed" status strings (capitalized as per schema)
✓ NULL closed_by_user_id for cascade closures
✓ byte for Priority (1-5)
✓ int user IDs (not strings)

---

## Build Status

```
✓ Solution builds: 0 errors, 0 warnings
✓ Database migrations applied
✓ Seed data loaded: 46 review tasks, 24 claims
✓ EF Core DbContext ready
✓ appsettings configured
✓ Program.cs wired for API
```

---

## To Run Locally

```bash
# Build and verify
cd F:\ClaimsQueue
dotnet build

# Run API (starts on https://localhost:5001)
dotnet run -p src/HealthcareClaimsQueue.API

# Run tests (when Phase 4 tests are added)
dotnet test
```

---

**Phase 1 Status: COMPLETE ✓**

**Proceed to Phase 2: Core API Endpoints**

Estimated time remaining: 3h 15m
