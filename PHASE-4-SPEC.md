# Phase 4: Metrics Implementation — SPECIFICATION

**Status:** 📋 READY FOR IMPLEMENTATION  
**Target:** SQL-based metrics queries + Reporting API  
**Estimated Effort:** ~2 hours  
**Tests:** 10 queries + validation suite  

---

## Overview

Phase 4 implements metrics and reporting for the Healthcare Claims Queue Manager. All queries are SQL-based with results returned via API endpoints for dashboard integration.

**Key Principle:** Metrics are computed from audit trails and task history, not from real-time database snapshots. This ensures accuracy and supports historical trend analysis.

---

## Required Metrics — 10 Queries

### Tier 1: MUST RUN (Critical for Operations)

**1. Claims Processed Per Reviewer**
```
SELECT 
    reviewer_user_id,
    DATE(closed_at) as review_date,
    COUNT(*) as claims_completed,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) as approved,
    COUNT(CASE WHEN outcome = 'PartialDenial' THEN 1 END) as partial_denial,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) as denied,
    AVG(DATEDIFF(SECOND, locked_on, closed_at)) as avg_lock_duration_sec
FROM review_tasks
WHERE closed_at IS NOT NULL
    AND closed_by_user_id IS NOT NULL
GROUP BY reviewer_user_id, DATE(closed_at)
ORDER BY review_date DESC, reviewer_user_id;
```
**Use Case:** Supervisor dashboard, reviewer productivity tracking
**Output Columns:** UserId, ReviewDate, ClaimsCompleted, Approved, PartialDenial, Denied, AvgLockDurationSec

**2. SLA Compliance by Queue**
```
SELECT 
    queue_id,
    DATE(closed_at) as completion_date,
    COUNT(*) as total_completed,
    COUNT(CASE WHEN closed_at <= due_date THEN 1 END) as within_sla,
    ROUND(100.0 * COUNT(CASE WHEN closed_at <= due_date THEN 1 END) / COUNT(*), 2) as sla_compliance_pct,
    COUNT(CASE WHEN closed_at > due_date THEN 1 END) as overdue_completed
FROM review_tasks
WHERE closed_at IS NOT NULL
GROUP BY queue_id, DATE(closed_at)
ORDER BY completion_date DESC, queue_id;
```
**Use Case:** Queue health monitoring, SLA tracking by queue
**Output Columns:** QueueId, CompletionDate, TotalCompleted, WithinSla, SlaCompliancePct, OverdueCompleted

**3. Current Queue Depth by Status**
```
SELECT 
    queue_id,
    status,
    COUNT(*) as task_count,
    CASE 
        WHEN status = 'Open' THEN 'Unassigned'
        WHEN status = 'Closed' THEN 'Completed'
        ELSE 'Other'
    END as status_category,
    MIN(created_at) as oldest_task_created_at,
    MAX(due_date) as latest_due_date
FROM review_tasks
WHERE status IN ('Open', 'Closed')
GROUP BY queue_id, status
ORDER BY queue_id, status;
```
**Use Case:** Real-time queue depth dashboard, capacity planning
**Output Columns:** QueueId, Status, TaskCount, StatusCategory, OldestTaskCreatedAt, LatestDueDate

---

### Tier 2: OPTIONAL (Schema Support, Analytics)

**4. Lock Contention Analysis**
```
SELECT 
    queue_id,
    DATE(locked_on) as lock_date,
    COUNT(*) as total_locks_acquired,
    AVG(DATEDIFF(SECOND, locked_on, locked_expires_on)) as avg_lock_duration_configured_sec,
    AVG(DATEDIFF(SECOND, locked_on, 
        CASE WHEN closed_at IS NOT NULL THEN closed_at ELSE locked_expires_on END)) as avg_actual_lock_held_sec,
    MAX(DATEDIFF(SECOND, locked_on, locked_expires_on)) as max_lock_duration_sec
FROM review_tasks
WHERE locked_on IS NOT NULL
    AND locked_by_user_id IS NOT NULL
GROUP BY queue_id, DATE(locked_on)
ORDER BY lock_date DESC;
```
**Use Case:** Performance tuning, lock duration configuration
**Output Columns:** QueueId, LockDate, TotalLocksAcquired, AvgLockDurationConfiguredSec, AvgActualLockHeldSec, MaxLockDurationSec

**5. Outcome Distribution by Queue**
```
SELECT 
    queue_id,
    outcome,
    COUNT(*) as count,
    ROUND(100.0 * COUNT(*) / SUM(COUNT(*)) OVER (PARTITION BY queue_id), 2) as pct_of_queue
FROM review_tasks
WHERE closed_at IS NOT NULL
GROUP BY queue_id, outcome
ORDER BY queue_id, COUNT(*) DESC;
```
**Use Case:** Quality metrics, understand denial/pend rates by queue
**Output Columns:** QueueId, Outcome, Count, PctOfQueue

**6. Reviewer Specialization (Outcome Success Rate)**
```
SELECT 
    closed_by_user_id as reviewer_user_id,
    COUNT(*) as total_reviews,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) as approved,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) / COUNT(*), 2) as approval_rate_pct,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) as denied,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) / COUNT(*), 2) as denial_rate_pct,
    COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) as pended,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) / COUNT(*), 2) as pend_rate_pct
FROM review_tasks
WHERE closed_at IS NOT NULL
    AND closed_by_user_id IS NOT NULL
GROUP BY closed_by_user_id
ORDER BY total_reviews DESC;
```
**Use Case:** Identify specialist reviewers, quality assurance
**Output Columns:** ReviewerUserId, TotalReviews, Approved, ApprovalRatePct, Denied, DenialRatePct, Pended, PendRatePct

**7. Pend Cycle Analysis (2nd Review Performance)**
```
SELECT 
    DATE(pended_at) as pend_date,
    COUNT(*) as pended_tasks,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) as pended_then_approved,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) as pended_then_denied,
    ROUND(AVG(DATEDIFF(DAY, pended_at, closed_at)), 2) as avg_days_to_completion_after_pend
FROM review_tasks
WHERE pended_at IS NOT NULL
    AND closed_at IS NOT NULL
GROUP BY DATE(pended_at)
ORDER BY pend_date DESC;
```
**Use Case:** Understand pend queue effectiveness, track claims through pend cycle
**Output Columns:** PendDate, PendedTasks, PendedThenApproved, PendedThenDenied, AvgDaysToCompletionAfterPend

**8. Review Time Distribution (Percentiles)**
```
SELECT 
    queue_id,
    PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) as p25_review_time_sec,
    PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) as p50_review_time_sec,
    PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) as p75_review_time_sec,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) as p95_review_time_sec,
    COUNT(*) as sample_size
FROM review_tasks
WHERE closed_at IS NOT NULL
    AND locked_on IS NOT NULL
GROUP BY queue_id
ORDER BY queue_id;
```
**Use Case:** Performance benchmarking, identify slow reviews
**Output Columns:** QueueId, P25ReviewTimeSec, P50ReviewTimeSec, P75ReviewTimeSec, P95ReviewTimeSec, SampleSize

**9. Task Age Distribution (SLA Risk Forecast)**
```
SELECT 
    queue_id,
    CASE 
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 3 THEN '0-3 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 7 THEN '4-7 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 14 THEN '8-14 days'
        ELSE '15+ days'
    END as age_bucket,
    COUNT(*) as task_count,
    COUNT(CASE WHEN assigned_to_user_id IS NOT NULL THEN 1 END) as assigned_count,
    COUNT(CASE WHEN assigned_to_user_id IS NULL THEN 1 END) as unassigned_count
FROM review_tasks
WHERE status = 'Open'
GROUP BY queue_id, 
    CASE 
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 3 THEN '0-3 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 7 THEN '4-7 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 14 THEN '8-14 days'
        ELSE '15+ days'
    END
ORDER BY queue_id, 
    CASE age_bucket
        WHEN '0-3 days' THEN 1
        WHEN '4-7 days' THEN 2
        WHEN '8-14 days' THEN 3
        ELSE 4
    END;
```
**Use Case:** Identify aging tasks that may breach SLA
**Output Columns:** QueueId, AgeBucket, TaskCount, AssignedCount, UnassignedCount

**10. Cascading Impact Analysis (Deny Outcomes)**
```
SELECT 
    DATE(closed_at) as completion_date,
    COUNT(*) as total_tasks_closed,
    COUNT(CASE WHEN closed_by_user_id IS NOT NULL THEN 1 END) as direct_reviewer_actions,
    COUNT(CASE WHEN closed_by_user_id IS NULL THEN 1 END) as cascade_closures,
    ROUND(100.0 * COUNT(CASE WHEN closed_by_user_id IS NULL THEN 1 END) / COUNT(*), 2) as cascade_pct
FROM review_tasks
WHERE closed_at IS NOT NULL
    AND outcome = 'Deny'
GROUP BY DATE(closed_at)
ORDER BY completion_date DESC;
```
**Use Case:** Understand cascade closure impact, efficiency of deny outcomes
**Output Columns:** CompletionDate, TotalTasksClosed, DirectReviewerActions, CascadeClosures, CascadePct

---

## API Endpoints (New)

All metrics endpoints require `X-User-Id` header.

### Get Reviewer Productivity
```
GET /api/metrics/reviewer-productivity?fromDate=2025-01-01&toDate=2025-01-31
Response: 200 OK
[
  {
    "userId": 1,
    "reviewDate": "2025-01-15",
    "claimsCompleted": 47,
    "approved": 38,
    "partialDenial": 7,
    "denied": 2,
    "avgLockDurationSec": 156
  }
]
```

### Get SLA Compliance
```
GET /api/metrics/sla-compliance?queueId=1&fromDate=2025-01-01&toDate=2025-01-31
Response: 200 OK
[
  {
    "queueId": 1,
    "completionDate": "2025-01-15",
    "totalCompleted": 45,
    "withinSla": 43,
    "slaCompliancePct": 95.56,
    "overdueCompleted": 2
  }
]
```

### Get Queue Depth
```
GET /api/metrics/queue-depth
Response: 200 OK
[
  {
    "queueId": 1,
    "status": "Open",
    "taskCount": 234,
    "statusCategory": "Unassigned",
    "oldestTaskCreatedAt": "2025-01-10T08:30:00Z",
    "latestDueDate": "2025-01-25T17:00:00Z"
  }
]
```

---

## Implementation Steps

1. **Create MetricsController.cs**
   - 3 endpoints: ReviewerProductivity, SlaCompliance, QueueDepth
   - All query from QueueDbContext using raw SQL or LINQ

2. **Add Metrics Service Interface**
   - IMetricsService with 3 methods returning DTOs
   - MetricsService implementation executing queries

3. **Create Response DTOs**
   - ReviewerProductivityDto
   - SlaComplianceDto
   - QueueDepthDto
   - Full list for all 10 queries

4. **Write Unit Tests**
   - Mock database with test data
   - Verify query results are correct
   - Test date range filtering
   - Test NULL handling

5. **Register in DI Container**
   - `builder.Services.AddScoped<IMetricsService, MetricsService>()`

---

## Testing Strategy

Each of the 10 queries requires:
- ✓ Unit test with in-memory database
- ✓ Verification of result shape and column names
- ✓ Edge cases (empty result set, NULL values, date boundaries)

Tests should seed realistic data:
- 100+ tasks across queues
- Multiple reviewers with varying approval rates
- Tasks in all status states
- Outcomes: Approve, PartialDenial, Deny, Pend

---

## Performance Considerations

- **Indexes:** Ensure indexes exist on (queue_id), (closed_at), (closed_by_user_id)
- **Materialized Views:** Consider for frequently-accessed queries (optional)
- **Pagination:** Not needed; metrics queries return aggregates
- **Caching:** Consider 5-minute cache for dashboard endpoints (optional)

---

## What's After Phase 4?

Phase 5 will:
- ✨ Add dashboard Razor Page to visualize metrics
- 🎨 Polish UI styling with responsive design
- ♿ Add accessibility features (ARIA labels, keyboard nav)
- 📚 Generate interview documentation

---

**Phase 4 Status: READY FOR IMPLEMENTATION**

This specification provides all 10 SQL queries ready to implement. Each query is verified logically and mapped to a use case. Implementation should take ~2 hours for all 3 endpoints + 10 supporting queries + tests.
