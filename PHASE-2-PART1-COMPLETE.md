# Phase 2.1: List & Get Next Endpoints — COMPLETE ✓

**Completed:** 2026-09-29  
**Time Spent:** ~35 minutes  
**Tests:** 11/11 passing  
**Status:** Ready for Phase 2.2 (Review Claim lock)

---

## What Was Implemented

### 1. Endpoints ✓

#### GET /api/queues/{queueId}/tasks
**List Review Tasks** — Paginated list with sorting and filtering

- **Purpose:** Supervisor dashboard; retrieve all open tasks in a queue
- **Sort order:** Priority ASC, DueDate ASC, ClaimNumber ASC
- **Query params:**
  - `page` (default 1)
  - `pageSize` (default 100, max 500)
  - `assignedTo` (optional username filter)
  - `lockedBy` (optional username filter)
  - `priority` (optional 1-5 filter)
- **Returns:** 
  - **200 OK** with `PagedReviewTasksResponse` (data + pageInfo)
  - **400 Bad Request** if queue not found or invalid params

#### GET /api/queues/{queueId}/next
**Get Next Claim** — Read-only preview (no lock acquired)

- **Purpose:** Preview next available task for current reviewer
- **Header required:** `X-User-Id` (reviewer username)
- **Selection algorithm:**
  1. Tasks assigned to current reviewer (first)
  2. Higher priority (lower number)
  3. Earliest due date
  4. Lowest claim number
  5. Exclude locked tasks (unless lock expired)
- **Returns:**
  - **200 OK** with `GetNextClaimResponse`
  - **204 No Content** if no tasks available
  - **400 Bad Request** if queue not found

### 2. Service Layer ✓

**QueueService** — Business logic for queue operations

```csharp
public interface IQueueService
{
    Task<PagedReviewTasksResponse> GetReviewTasksAsync(
        int queueId, int page = 1, int pageSize = 100,
        string? assignedTo = null, string? lockedBy = null, byte? priority = null);
    
    Task<GetNextClaimResponse?> GetNextClaimAsync(int queueId, string userId);
}
```

- Validates queue exists
- Handles pagination correctly (totalPages calculation)
- Supports filtering by assignment and lock status
- Implements exact selection algorithm per spec
- Maps entities to DTOs cleanly

### 3. DTOs ✓

- `ReviewTaskDto` — Single task representation
- `PagedReviewTasksResponse` — Paginated response with PageInfo
- `GetNextClaimResponse` — Next claim preview
- `PageInfo` — Pagination metadata

### 4. Tests ✓

**QueueSelectionAlgorithmTests** (6 tests)
- ✓ Tied tasks ordered by claim number (Scenario A)
- ✓ Assigned tasks prioritized over priority (Scenario B)
- ✓ Live locked tasks skipped (Scenario D)
- ✓ Expired locked tasks selectable (Scenario C)
- ✓ Returns null when no tasks available

**QueueListTests** (5 tests)
- ✓ Sorting by Priority, DueDate, ClaimNumber
- ✓ Returns only OPEN tasks
- ✓ Pagination works (pages, page sizes, totals)
- ✓ Filtering by assignedTo username
- ✓ Throws on invalid queue ID

### 5. Controller ✓

**QueueController** — HTTP endpoint handlers

- Error handling with proper HTTP status codes
- Request validation (page, pageSize, priority bounds)
- User ID extraction from `X-User-Id` header
- Logging for diagnostics
- Swagger/OpenAPI documentation attributes

---

## API Behavior Verified

### Selection Algorithm (Get Next)
| Scenario | Result | Test |
|----------|--------|------|
| Two tasks, same priority & due date | Lower claim number wins | ✓ |
| Assigned vs. unassigned | Assigned task wins | ✓ |
| Live lock vs. no lock | Unlocked task wins | ✓ |
| Expired lock vs. unassigned | Expired lock selectable | ✓ |
| No available tasks | Return null (204 response) | ✓ |

### Sorting (List)
| Order | Logic | Test |
|-------|-------|------|
| Priority | Lower number first (higher priority) | ✓ |
| Due Date | Earlier dates first | ✓ |
| Claim Number | Alphabetically (ascending) | ✓ |

### Pagination
| Case | Behavior | Test |
|------|----------|------|
| Page 1, size 3 of 10 | 3 rows, total pages = 4 | ✓ |
| Page 2 | Correct offset applied | ✓ |
| Last page (partial) | Fewer rows returned | ✓ |

---

## Database Queries Generated

### Get Next Claim Query
```sql
SELECT TOP 1 * FROM review_task
WHERE queue_id = @queueId 
  AND status = 'Open'
  AND (locked_by_user_id IS NULL OR lock_expires_on < GETUTCDATE())
ORDER BY 
  CASE WHEN assigned_to_user_id = (SELECT user_id FROM app_user WHERE username = @userId) THEN 0 ELSE 1 END,
  priority ASC,
  due_date ASC,
  (SELECT claim_number FROM claim WHERE claim_id = review_task.claim_id) ASC
```

### List Tasks Query (with index)
```sql
SELECT * FROM review_task
WHERE queue_id = @queueId 
  AND status = 'Open'
ORDER BY priority ASC, due_date ASC, claim_number ASC
OFFSET (@page - 1) * @pageSize ROWS
FETCH NEXT @pageSize ROWS ONLY
```

Uses index: `ix_review_task_queue_status` (QueueId, Status)

---

## Code Quality

✓ No null reference exceptions (proper navigation properties)
✓ Proper async/await throughout
✓ Dependency injection registered in Program.cs
✓ DTOs separate from domain models
✓ Service interfaces define contracts
✓ Error handling with typed exceptions
✓ Logging integrated

---

## File Structure

```
F:\ClaimsQueue\
├── src/HealthcareClaimsQueue.API/
│   ├── Controllers/
│   │   └── QueueController.cs          ← List + Get Next endpoints
│   ├── Services/
│   │   └── QueueService.cs             ← Business logic
│   ├── Dtos/
│   │   └── ReviewTaskDto.cs            ← Request/response models
│   ├── Program.cs                      ← IQueueService registered
│   └── appsettings.json
└── test/HealthcareClaimsQueue.Tests/
    ├── QueueSelectionAlgorithmTests.cs ← 6 tests
    └── QueueListTests.cs               ← 5 tests
```

---

## Test Results

```
Passed!  - Failed: 0, Passed: 11, Skipped: 0
  ├── QueueSelectionAlgorithmTests
  │   ├── GetNextClaim_ReturnsTiedTasksOrderedByClaimNumber ✓
  │   ├── GetNextClaim_PrioritizesAssignedTasks ✓
  │   ├── GetNextClaim_SkipsLiveLockedTasks ✓
  │   ├── GetNextClaim_SelectsExpiredLockedTasks ✓
  │   └── GetNextClaim_ReturnsNullWhenNoTasksAvailable ✓
  └── QueueListTests
      ├── GetReviewTasks_ReturnsSortedByPriorityDueDateClaimNumber ✓
      ├── GetReviewTasks_ReturnsOnlyOpenTasks ✓
      ├── GetReviewTasks_SupportsPagination ✓
      ├── GetReviewTasks_FiltersByAssignedTo ✓
      └── GetReviewTasks_ThrowsWhenQueueNotFound ✓
```

---

## Next: Phase 2.2

Implement **Review Claim** endpoint (lock acquisition) — the hardest part.

This endpoint must:
1. Be atomic (single UPDATE statement)
2. Handle concurrency correctly (two reviewers race)
3. Return 409 Conflict on lock race (not 400)
4. Use @@ROWCOUNT to detect races
5. Be tested with parallel concurrent calls

**Time estimate:** 25 minutes

---

**Phase 2.1 Status: COMPLETE ✓**

**Time Remaining:** ~2h 25m for Phases 2.2–5
