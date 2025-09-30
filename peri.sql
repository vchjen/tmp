;WITH parsed AS (
    SELECT
        a.id                                     AS EntityId,
        a.date                                   AS AuditDate,
        a.op_code                                AS OpCode,
        CAST(JSON_VALUE(a.old, '$.note') AS NVARCHAR(MAX)) AS OldNote,
        CAST(JSON_VALUE(a.new, '$.note') AS NVARCHAR(MAX)) AS NewNote,
        JSON_VALUE(a.old, '$.modifiedby')        AS OldModifiedBy,
        JSON_VALUE(a.new, '$.modifiedby')        AS NewModifiedBy,
        TRY_CONVERT(datetime2, JSON_VALUE(a.old, '$.date')) AS OldNoteDate,
        TRY_CONVERT(datetime2, JSON_VALUE(a.new, '$.date')) AS NewNoteDate
    FROM dbo.audit a
),
norm AS (
    SELECT
        p.*,
        -- Normalize CRLF/CR to LF for robust comparisons
        CAST(REPLACE(REPLACE(ISNULL(p.OldNote, N''), CHAR(13) + CHAR(10), CHAR(10)), CHAR(13), CHAR(10)) AS NVARCHAR(MAX)) AS OldN,
        CAST(REPLACE(REPLACE(ISNULL(p.NewNote, N''), CHAR(13) + CHAR(10), CHAR(10)), CHAR(13), CHAR(10)) AS NVARCHAR(MAX)) AS NewN
    FROM parsed p
),
delta AS (
    SELECT
        n.*,
        CASE 
            WHEN n.OldN = N'' THEN n.NewN
            WHEN LEFT(n.NewN, LEN(n.OldN)) = n.OldN 
                THEN STUFF(n.NewN, 1, LEN(n.OldN), N'')                -- pure prefix, keep the suffix
            WHEN LEFT(n.NewN, LEN(n.OldN) + 1) = n.OldN + NCHAR(10)
                THEN SUBSTRING(n.NewN, LEN(n.OldN) + 2, LEN(n.NewN))    -- prefix + LF, drop both
            ELSE n.NewN  -- fallback: not a clean append; treat whole New as the delta
        END AS AddedN
    FROM norm n
),
clean AS (
    SELECT
        d.*,
        -- Trim a leading LF if present after stripping
        CASE WHEN LEFT(d.AddedN, 1) = NCHAR(10)
             THEN NULLIF(SUBSTRING(d.AddedN, 2, LEN(d.AddedN)), N'')
             ELSE NULLIF(d.AddedN, N'')
        END AS AddedNote
    FROM delta d
)
SELECT
    EntityId,
    AddedNote,                                    -- this is (new_note - old_note)
    NewModifiedBy   AS ModifiedBy,                 -- who added it
    COALESCE(NewNoteDate, AuditDate) AS ModifiedAt,
    OpCode,
    AuditDate       AS AuditRowTime
FROM clean
WHERE ISNULL(OldNote, N'') <> ISNULL(NewNote, N'')     -- note changed
  AND AddedNote IS NOT NULL                            -- ignore pure deletions / no actual append
-- Optional: keep only pure appends (new longer than old)
--  AND LEN(NewN) > LEN(OldN)
ORDER BY EntityId, COALESCE(NewNoteDate, AuditDate), AuditRowTime;
