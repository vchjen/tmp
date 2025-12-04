CREATE VIEW dbo.AuditJsonDiff
AS
SELECT
    a.audit_id,
    a.op_code,
    a.op_date,
    d.diff_op,          -- 'add' / 'remove' / 'replace'
    d.[key]      AS json_path,   -- top-level key
    d.old_value,
    d.new_value
FROM dbo.AuditLog a
CROSS APPLY (
    SELECT
        CASE 
            WHEN o.[key] IS NULL THEN 'add'       -- only in new_json
            WHEN n.[key] IS NULL THEN 'remove'    -- only in old_json
            ELSE 'replace'                        -- in both but different
        END                         AS diff_op,
        ISNULL(o.[key], n.[key])    AS [key],
        o.value                     AS old_value,
        n.value                     AS new_value
    FROM OPENJSON(a.old_json) o
    FULL OUTER JOIN OPENJSON(a.new_json) n
        ON o.[key] = n.[key]
) d
WHERE
    -- keep only real differences
    (d.old_value IS NULL OR d.new_value IS NULL OR d.old_value <> d.new_value);
GO
