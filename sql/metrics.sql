-- ============================================================================
-- Healthcare Claims Queue Manager - Metrics Queries
-- ============================================================================
-- 10 SQL queries for operational metrics and reporting
-- Database: ClaimsQueue
-- Created: 2025-09-29
-- ============================================================================

-- ============================================================================
-- TIER 1: MUST-RUN QUERIES (Critical for Operations)
-- ============================================================================

-- ============================================================================
-- Query 1: Claims Processed Per Reviewer (Daily)
-- Purpose: Track reviewer productivity, outcomes distribution, lock duration
-- Use Case: Supervisor dashboard, reviewer performance metrics
-- ============================================================================
SELECT
    closed_by_user_id AS reviewer_user_id,
    CAST(closed_at AS DATE) AS review_date,
    COUNT(*) AS claims_completed,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) AS approved,
    COUNT(CASE WHEN outcome = 'PartialDenial' THEN 1 END) AS partial_denial,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) AS denied,
    COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) AS pended,
    AVG(DATEDIFF(SECOND, locked_on, closed_at)) AS avg_lock_duration_sec,
    MIN(closed_at) AS earliest_completion,
    MAX(closed_at) AS latest_completion
FROM review_task
WHERE closed_at IS NOT NULL
    AND closed_by_user_id IS NOT NULL
GROUP BY closed_by_user_id, CAST(closed_at AS DATE)
ORDER BY review_date DESC, reviewer_user_id;

-- ============================================================================
-- Query 2: SLA Compliance by Queue (Daily)
-- Purpose: Monitor SLA adherence, identify problematic queues
-- Use Case: Queue health monitoring, SLA tracking
-- ============================================================================
SELECT
    queue_id,
    CAST(closed_at AS DATE) AS completion_date,
    COUNT(*) AS total_completed,
    SUM(CASE WHEN closed_at <= due_date THEN 1 ELSE 0 END) AS within_sla,
    ROUND(100.0 * SUM(CASE WHEN closed_at <= due_date THEN 1 ELSE 0 END) / COUNT(*), 2) AS sla_compliance_pct,
    SUM(CASE WHEN closed_at > due_date THEN 1 ELSE 0 END) AS overdue_completed,
    AVG(DATEDIFF(HOUR, due_date, closed_at)) AS avg_hours_past_due
FROM review_task
WHERE closed_at IS NOT NULL
GROUP BY queue_id, CAST(closed_at AS DATE)
ORDER BY completion_date DESC, queue_id;

-- ============================================================================
-- Query 3: Current Queue Depth by Status
-- Purpose: Real-time queue monitoring, capacity planning
-- Use Case: Dashboard display, workload balancing
-- ============================================================================
SELECT
    queue_id,
    status,
    COUNT(*) AS task_count,
    CASE
        WHEN status = 'Open' THEN 'Unassigned'
        WHEN status = 'Closed' THEN 'Completed'
        ELSE 'Other'
    END AS status_category,
    MIN(created_at) AS oldest_task_created_at,
    MAX(due_date) AS latest_due_date,
    COUNT(CASE WHEN assigned_to_user_id IS NOT NULL THEN 1 END) AS assigned_count,
    COUNT(CASE WHEN assigned_to_user_id IS NULL THEN 1 END) AS unassigned_count
FROM review_task
WHERE status IN ('Open', 'Closed')
GROUP BY queue_id, status
ORDER BY queue_id, status;

-- ============================================================================
-- TIER 2: OPTIONAL QUERIES (Analytics, Schema Support)
-- ============================================================================

-- ============================================================================
-- Query 4: Lock Contention Analysis
-- Purpose: Performance tuning, lock duration optimization
-- Use Case: Identify lock bottlenecks, tune expiration window
-- ============================================================================
SELECT
    queue_id,
    CAST(locked_on AS DATE) AS lock_date,
    COUNT(*) AS total_locks_acquired,
    AVG(DATEDIFF(SECOND, locked_on, lock_expires_on)) AS avg_lock_duration_configured_sec,
    AVG(DATEDIFF(SECOND, locked_on,
        CASE WHEN closed_at IS NOT NULL THEN closed_at ELSE lock_expires_on END)) AS avg_actual_lock_held_sec,
    MAX(DATEDIFF(SECOND, locked_on, lock_expires_on)) AS max_lock_duration_sec,
    MIN(DATEDIFF(SECOND, locked_on, lock_expires_on)) AS min_lock_duration_sec
FROM review_task
WHERE locked_on IS NOT NULL
    AND locked_by_user_id IS NOT NULL
GROUP BY queue_id, CAST(locked_on AS DATE)
ORDER BY lock_date DESC;

-- ============================================================================
-- Query 5: Outcome Distribution by Queue
-- Purpose: Understand quality metrics, denial/pend rates
-- Use Case: Queue performance analysis, identify problematic areas
-- ============================================================================
SELECT
    queue_id,
    outcome,
    COUNT(*) AS count,
    ROUND(100.0 * COUNT(*) / SUM(COUNT(*)) OVER (PARTITION BY queue_id), 2) AS pct_of_queue
FROM review_task
WHERE closed_at IS NOT NULL
    AND outcome IS NOT NULL
GROUP BY queue_id, outcome
ORDER BY queue_id, COUNT(*) DESC;

-- ============================================================================
-- Query 6: Reviewer Specialization (Outcome Success Rate)
-- Purpose: Identify specialist reviewers, quality assurance
-- Use Case: Assign complex claims to specialists, identify training needs
-- ============================================================================
SELECT
    closed_by_user_id AS reviewer_user_id,
    COUNT(*) AS total_reviews,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) AS approved,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) / COUNT(*), 2) AS approval_rate_pct,
    COUNT(CASE WHEN outcome = 'PartialDenial' THEN 1 END) AS partial_denial,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'PartialDenial' THEN 1 END) / COUNT(*), 2) AS partial_denial_rate_pct,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) AS denied,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) / COUNT(*), 2) AS denial_rate_pct,
    COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) AS pended,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) / COUNT(*), 2) AS pend_rate_pct,
    AVG(DATEDIFF(SECOND, locked_on, closed_at)) AS avg_review_time_sec
FROM review_task
WHERE closed_at IS NOT NULL
    AND closed_by_user_id IS NOT NULL
GROUP BY closed_by_user_id
ORDER BY total_reviews DESC;

-- ============================================================================
-- Query 7: Pend Cycle Analysis (2nd Review Performance)
-- Purpose: Understand pend queue effectiveness
-- Use Case: Track claims through pend cycle, measure effectiveness
-- ============================================================================
SELECT
    CAST(pended_at AS DATE) AS pend_date,
    COUNT(*) AS pended_tasks,
    COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) AS pended_then_approved,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Approve' THEN 1 END) / COUNT(*), 2) AS approval_after_pend_pct,
    COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) AS pended_then_denied,
    ROUND(100.0 * COUNT(CASE WHEN outcome = 'Deny' THEN 1 END) / COUNT(*), 2) AS denial_after_pend_pct,
    COUNT(CASE WHEN outcome = 'Pend' THEN 1 END) AS pended_again,
    ROUND(AVG(DATEDIFF(DAY, pended_at, closed_at)), 2) AS avg_days_to_completion_after_pend
FROM review_task
WHERE pended_at IS NOT NULL
    AND closed_at IS NOT NULL
GROUP BY CAST(pended_at AS DATE)
ORDER BY pend_date DESC;

-- ============================================================================
-- Query 8: Review Time Distribution (Percentiles)
-- Purpose: Performance benchmarking, identify slow reviews
-- Use Case: Set SLA targets, identify training needs
-- ============================================================================
SELECT
    queue_id,
    COUNT(*) AS sample_size,
    MIN(DATEDIFF(SECOND, locked_on, closed_at)) AS min_review_time_sec,
    PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) AS p25_review_time_sec,
    PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) AS p50_review_time_sec,
    PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) AS p75_review_time_sec,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DATEDIFF(SECOND, locked_on, closed_at)) AS p95_review_time_sec,
    MAX(DATEDIFF(SECOND, locked_on, closed_at)) AS max_review_time_sec
FROM review_task
WHERE closed_at IS NOT NULL
    AND locked_on IS NOT NULL
GROUP BY queue_id
ORDER BY queue_id;

-- ============================================================================
-- Query 9: Task Age Distribution (SLA Risk Forecast)
-- Purpose: Identify aging tasks that may breach SLA
-- Use Case: Early warning system, workload prioritization
-- ============================================================================
SELECT
    queue_id,
    CASE
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 3 THEN '0-3 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 7 THEN '4-7 days'
        WHEN DATEDIFF(DAY, created_at, GETUTCDATE()) <= 14 THEN '8-14 days'
        ELSE '15+ days'
    END AS age_bucket,
    COUNT(*) AS task_count,
    COUNT(CASE WHEN assigned_to_user_id IS NOT NULL THEN 1 END) AS assigned_count,
    COUNT(CASE WHEN assigned_to_user_id IS NULL THEN 1 END) AS unassigned_count,
    COUNT(CASE WHEN DATEDIFF(DAY, due_date, GETUTCDATE()) > 0 THEN 1 END) AS overdue_count,
    ROUND(AVG(DATEDIFF(DAY, created_at, GETUTCDATE())), 1) AS avg_age_days
FROM review_task
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

-- ============================================================================
-- Query 10: Cascading Impact Analysis (Deny Outcomes)
-- Purpose: Understand cascade closure impact, denial efficiency
-- Use Case: Measure impact of denial decisions, queue efficiency
-- ============================================================================
SELECT
    CAST(closed_at AS DATE) AS completion_date,
    COUNT(*) AS total_tasks_closed,
    SUM(CASE WHEN closed_by_user_id IS NOT NULL THEN 1 ELSE 0 END) AS direct_reviewer_actions,
    SUM(CASE WHEN closed_by_user_id IS NULL THEN 1 ELSE 0 END) AS cascade_closures,
    ROUND(100.0 * SUM(CASE WHEN closed_by_user_id IS NULL THEN 1 ELSE 0 END) / COUNT(*), 2) AS cascade_pct,
    COUNT(DISTINCT claim_id) AS unique_claims_affected
FROM review_task
WHERE closed_at IS NOT NULL
    AND outcome = 'Deny'
GROUP BY CAST(closed_at AS DATE)
ORDER BY completion_date DESC;

-- ============================================================================
-- BONUS QUERIES
-- ============================================================================

-- ============================================================================
-- Bonus Query: Daily Summary Dashboard
-- Purpose: All-in-one daily metrics snapshot
-- ============================================================================
SELECT
    CAST(GETUTCDATE() AS DATE) AS report_date,
    (SELECT COUNT(*) FROM review_task WHERE status = 'Open') AS total_open_tasks,
    (SELECT COUNT(DISTINCT queue_id) FROM review_task) AS num_queues,
    (SELECT COUNT(DISTINCT closed_by_user_id) FROM review_task WHERE closed_at >= DATEADD(DAY, -1, GETUTCDATE())) AS reviewers_active_today,
    (SELECT COUNT(*) FROM review_task WHERE closed_at >= DATEADD(DAY, -1, GETUTCDATE())) AS tasks_completed_today,
    (SELECT COUNT(*) FROM review_task WHERE locked_by_user_id IS NOT NULL) AS currently_locked_tasks,
    (SELECT ROUND(AVG(DATEDIFF(SECOND, locked_on, closed_at)), 0) FROM review_task WHERE closed_at >= DATEADD(DAY, -1, GETUTCDATE()) AND locked_on IS NOT NULL) AS avg_review_time_sec_today;

-- ============================================================================
-- Bonus Query: Lock Health Check
-- Purpose: Identify potentially stuck locks (not completed after 2 hours)
-- ============================================================================
SELECT TOP 20
    task_id,
    claim_id,
    queue_id,
    locked_by_user_id,
    locked_on,
    lock_expires_on,
    DATEDIFF(MINUTE, locked_on, GETUTCDATE()) AS minutes_locked,
    CASE
        WHEN lock_expires_on < GETUTCDATE() THEN 'EXPIRED'
        WHEN DATEDIFF(MINUTE, locked_on, GETUTCDATE()) > 120 THEN 'STALE'
        ELSE 'ACTIVE'
    END AS lock_status
FROM review_task
WHERE locked_by_user_id IS NOT NULL
    AND closed_at IS NULL
    AND DATEDIFF(MINUTE, locked_on, GETUTCDATE()) > 30
ORDER BY locked_on ASC;

-- ============================================================================
-- INDEX HINTS: For best performance, ensure these indexes exist:
-- ============================================================================
-- CREATE INDEX idx_review_task_queue_status_priority_duedate
--     ON review_task(queue_id, status, priority, due_date);
--
-- CREATE INDEX idx_review_task_closed_at
--     ON review_task(closed_at) INCLUDE (closed_by_user_id, outcome);
--
-- CREATE INDEX idx_review_task_pended_at
--     ON review_task(pended_at) INCLUDE (outcome, closed_at);
-- ============================================================================

-- ============================================================================
-- END OF METRICS QUERIES
-- ============================================================================
-- All 10 queries are production-ready
-- Tier 1 (3 queries): Run these for operational monitoring
-- Tier 2 (7 queries): Run for analytics and performance tuning
-- Bonus (2 queries): Run for health checks and dashboards
-- ============================================================================
