DECLARE 
    @old nvarchar(max) = N'{"address":"old","city":"Paris","price":10}',
    @new nvarchar(max) = N'{"address":"new","price":10,"zip":123}';

-- DIFF: only changed / added / removed keys
SELECT
    '{' +
    STRING_AGG(
        '"' + d.[key] + '":' +
        CASE 
            WHEN d.new_value IS NULL THEN 'null'
            ELSE '"' + d.new_value + '"'
        END
    , ',') 
    + '}' AS diff
FROM (
    SELECT
        ISNULL(o.[key], n.[key]) AS [key],
        o.value AS old_value,
        n.value AS new_value
    FROM OPENJSON(@old) o
    FULL OUTER JOIN OPENJSON(@new) n
        ON o.[key] = n.[key]
) d
WHERE
      d.old_value IS NULL          -- added
   OR d.new_value IS NULL          -- removed
   OR d.old_value <> d.new_value   -- changed;
