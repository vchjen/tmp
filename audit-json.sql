-- AuditLog:
-- id, op_date, op_code (I/U/D), old_data (json), new_data (json)

SELECT
    a.id,
    a.op_date,
    a.op_code,
    a.old_data,
    a.new_data,
    CASE 
        WHEN a.op_code = 'I' THEN a.new_data  -- insert: whole new_data
        WHEN a.op_code = 'D' THEN NULL        -- delete: nothing (as you asked)
        ELSE diff.json_diff                   -- update: only changed fields
    END AS diff_json
FROM dbo.AuditLog a
OUTER APPLY (
    SELECT
        CASE 
            WHEN COUNT(*) = 0 THEN NULL
            ELSE
                '{' + STRING_AGG(
                        QUOTENAME(d.[key], '"') + ':' +
                        CASE 
                            WHEN d.new_value IS NULL THEN 'null'
                            ELSE '"' + REPLACE(d.new_value, '"', '\"') + '"'
                        END,
                        ','
                     ) + '}'
        END AS json_diff
    FROM (
        SELECT
            ISNULL(o.[key], n.[key]) AS [key],
            o.value AS old_value,
            n.value AS new_value
        FROM OPENJSON(a.old_data) o
        FULL OUTER JOIN OPENJSON(a.new_data) n
            ON o.[key] = n.[key]
    ) d
    WHERE
        d.old_value IS NULL
        OR d.new_value IS NULL
        OR d.old_value <> d.new_value
) diff;
