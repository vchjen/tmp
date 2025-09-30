SELECT
    nh.EntityId AS Id,
    (
        SELECT
            nh2.NoteId   AS noteid,
            nh2.Note     AS note_text,
            nh2.ModifiedBy AS addedby,
            nh2.ModifiedAt AS addedat
        FROM dbo.NoteHistory nh2
        WHERE nh2.EntityId = nh.EntityId
        ORDER BY nh2.ModifiedAt, nh2.NoteId
        FOR JSON PATH
    ) AS NotesJson
FROM dbo.NoteHistory nh
GROUP BY nh.EntityId;
