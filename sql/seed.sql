/* Healthcare Claims Queue Manager - Seed Data */
SET NOCOUNT ON;
GO

-- Queue reference data
SET IDENTITY_INSERT dbo.queue ON;
INSERT INTO dbo.queue (queue_id, queue_code, queue_name) VALUES
    (1, 'DUP',   N'Duplicate Check'),
    (2, 'AUTH',  N'Authorization Matching'),
    (3, 'CASE',  N'Case Rate'),
    (4, 'BENE',  N'Benefit Included in Another Service'),
    (5, 'PEND',  N'Pend'),
    (6, 'OTHER', N'Other');
SET IDENTITY_INSERT dbo.queue OFF;
GO

-- AppUser reference data
SET IDENTITY_INSERT dbo.app_user ON;
INSERT INTO dbo.app_user (user_id, username, display_name, role) VALUES
    (1, 'sfranklin',  N'Sarah Franklin',  'Supervisor'),
    (2, 'ereyes',     N'Elena Reyes',     'Reviewer'),
    (3, 'jchen',      N'Jason Chen',      'Reviewer'),
    (4, 'mkowalski',  N'Marta Kowalski',  'Reviewer'),
    (5, 'apatel',     N'Arjun Patel',     'Reviewer'),
    (6, 'dnguyen',    N'Dana Nguyen',     'Reviewer'),
    (7, 'lbecker',    N'Luis Becker',     'Reviewer'),
    (8, 'tosei',      N'Tom Osei',        'Reviewer');
SET IDENTITY_INSERT dbo.app_user OFF;
GO

-- Sample claims
DECLARE @today DATE = CAST(SYSUTCDATETIME() AS DATE);

SET IDENTITY_INSERT dbo.claim ON;
INSERT INTO dbo.claim (claim_id, claim_number, member_id, provider_id, billed_amount, service_from, service_to, received_on) VALUES
    ( 1, '0000418822', 'M00102341', 'P4471',  1240.00, DATEADD(day,-40,@today), DATEADD(day,-40,@today), DATEADD(day,-12,SYSUTCDATETIME())),
    ( 2, '0000418799', 'M00119088', 'P2210',   380.50, DATEADD(day,-38,@today), DATEADD(day,-38,@today), DATEADD(day,-12,SYSUTCDATETIME())),
    ( 3, '0000419044', 'M00102341', 'P4471',  1240.00, DATEADD(day,-40,@today), DATEADD(day,-40,@today), DATEADD(day,-11,SYSUTCDATETIME())),
    ( 4, '0000418501', 'M00133702', 'P8815', 18750.00, DATEADD(day,-52,@today), DATEADD(day,-45,@today), DATEADD(day,-14,SYSUTCDATETIME())),
    ( 5, '0000419310', 'M00140556', 'P3390',  6420.75, DATEADD(day,-30,@today), DATEADD(day,-28,@today), DATEADD(day,-10,SYSUTCDATETIME())),
    ( 6, '0000418640', 'M00151223', 'P1104',   225.00, DATEADD(day,-36,@today), DATEADD(day,-36,@today), DATEADD(day,-13,SYSUTCDATETIME())),
    ( 7, '0000419127', 'M00133702', 'P8815',  9300.00, DATEADD(day,-44,@today), DATEADD(day,-41,@today), DATEADD(day,-11,SYSUTCDATETIME())),
    ( 8, '0000418355', 'M00160914', 'P5528',   790.25, DATEADD(day,-33,@today), DATEADD(day,-33,@today), DATEADD(day,-15,SYSUTCDATETIME())),
    ( 9, '0000419488', 'M00172380', 'P6641',  3110.00, DATEADD(day,-29,@today), DATEADD(day,-27,@today), DATEADD(day, -9,SYSUTCDATETIME())),
    (10, '0000418913', 'M00119088', 'P2210',   380.50, DATEADD(day,-38,@today), DATEADD(day,-38,@today), DATEADD(day,-12,SYSUTCDATETIME())),
    (11, '0000418277', 'M00184455', 'P7702',   615.00, DATEADD(day,-47,@today), DATEADD(day,-47,@today), DATEADD(day,-16,SYSUTCDATETIME())),
    (12, '0000419602', 'M00190017', 'P8815', 24100.00, DATEADD(day,-26,@today), DATEADD(day,-14,@today), DATEADD(day, -8,SYSUTCDATETIME()));
SET IDENTITY_INSERT dbo.claim OFF;
GO

PRINT 'Seed data inserted successfully!';
SELECT 'Queue count:' AS [Status], COUNT(*) FROM dbo.queue;
SELECT 'User count:' AS [Status], COUNT(*) FROM dbo.app_user;
SELECT 'Claim count:' AS [Status], COUNT(*) FROM dbo.claim;
