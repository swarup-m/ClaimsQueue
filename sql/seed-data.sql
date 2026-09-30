-- Healthcare Claims Queue Manager - Seed Data (SQL Server Compatible)
-- Fixed version with proper SQL Server syntax

-- Step 1: Seed Users
IF NOT EXISTS (SELECT 1 FROM app_user WHERE user_id = 1)
BEGIN
    INSERT INTO app_user (user_id, username, display_name, role) VALUES
    (1, 'ereyes', 'Elena Reyes', 'Reviewer'),
    (2, 'jchen', 'Jason Chen', 'Reviewer'),
    (3, 'msmith', 'Maria Smith', 'Reviewer'),
    (4, 'pjohnson', 'Paul Johnson', 'Supervisor'),
    (5, 'swilson', 'Sarah Wilson', 'Admin');
    PRINT 'Seeded 5 users';
END;

-- Step 2: Seed Queues
IF NOT EXISTS (SELECT 1 FROM queue WHERE queue_id = 1)
BEGIN
    INSERT INTO queue (queue_id, queue_code, queue_name) VALUES
    (1, 'DUP', 'Duplicate Check'),
    (2, 'AUTH', 'Authorization Matching'),
    (3, 'CASE', 'Case Rate'),
    (5, 'PEND', 'Pending');
    PRINT 'Seeded 4 queues';
END;

-- Step 3: Seed Claims
IF NOT EXISTS (SELECT 1 FROM claim WHERE claim_id = 1)
BEGIN
    DECLARE @i INT = 1;
    WHILE @i <= 100
    BEGIN
        INSERT INTO claim (claim_number, member_id, provider_id, billed_amount, service_from, service_to, received_on)
        VALUES
        (
            RIGHT('0000000' + CAST(@i AS VARCHAR), 7),
            'M' + RIGHT('000000' + CAST(@i AS VARCHAR), 6),
            'P' + RIGHT('000000' + CAST(@i AS VARCHAR), 6),
            CAST(100 + (@i * 5) AS DECIMAL(10,2)),
            DATEADD(DAY, -30, GETUTCDATE()),
            DATEADD(DAY, -29, GETUTCDATE()),
            DATEADD(DAY, -7, GETUTCDATE())
        );
        SET @i = @i + 1;
    END;
    PRINT 'Seeded 100 claims';
END;

-- Step 4: Seed Review Tasks
IF NOT EXISTS (SELECT 1 FROM review_task WHERE claim_id = 1)
BEGIN
    -- Open unassigned tasks in DUP queue
    INSERT INTO review_task (claim_id, queue_id, priority, due_date, status, created_at)
    SELECT TOP 50
        c.claim_id,
        1,  -- DUP queue
        CASE WHEN c.claim_id % 10 = 0 THEN 1 ELSE 2 END,
        DATEADD(HOUR, 48, GETUTCDATE()),
        'Open',
        GETUTCDATE()
    FROM claim c
    ORDER BY c.claim_id;

    -- Open assigned tasks in AUTH queue
    INSERT INTO review_task (claim_id, queue_id, priority, due_date, status, assigned_to_user_id, created_at)
    SELECT TOP 30
        c.claim_id,
        2,  -- AUTH queue
        CASE WHEN c.claim_id % 5 = 0 THEN 1 ELSE 2 END,
        DATEADD(HOUR, 36, GETUTCDATE()),
        'Open',
        1 + (c.claim_id % 3),
        DATEADD(DAY, -1, GETUTCDATE())
    FROM claim c
    WHERE c.claim_id > 50
    ORDER BY c.claim_id;

    -- Closed tasks in CASE queue
    INSERT INTO review_task (claim_id, queue_id, priority, due_date, status, outcome, closed_by_user_id, closed_at, created_at)
    SELECT TOP 20
        c.claim_id,
        3,  -- CASE queue
        2,
        DATEADD(DAY, -2, GETUTCDATE()),
        'Closed',
        CASE WHEN c.claim_id % 4 = 0 THEN 'Approve'
             WHEN c.claim_id % 4 = 1 THEN 'PartialDenial'
             WHEN c.claim_id % 4 = 2 THEN 'Deny'
             ELSE 'Pend' END,
        1 + (c.claim_id % 3),
        DATEADD(HOUR, -12, GETUTCDATE()),
        DATEADD(DAY, -3, GETUTCDATE())
    FROM claim c
    WHERE c.claim_id > 80
    ORDER BY c.claim_id;

    -- Locked tasks
    INSERT INTO review_task (claim_id, queue_id, priority, due_date, status, locked_by_user_id, locked_on, lock_expires_on, created_at)
    SELECT TOP 5
        c.claim_id,
        1,  -- DUP queue
        1,
        DATEADD(HOUR, 24, GETUTCDATE()),
        'Open',
        1 + (c.claim_id % 3),
        DATEADD(MINUTE, -5, GETUTCDATE()),
        DATEADD(MINUTE, 10, GETUTCDATE()),
        GETUTCDATE()
    FROM claim c
    WHERE c.claim_id > 95
    ORDER BY c.claim_id;

    PRINT 'Seeded 105 review tasks';
END;

-- Verification
SELECT
    (SELECT COUNT(*) FROM app_user) AS users,
    (SELECT COUNT(*) FROM queue) AS queues,
    (SELECT COUNT(*) FROM claim) AS claims,
    (SELECT COUNT(*) FROM review_task) AS tasks;

PRINT '✅ Seed data loaded successfully!';
