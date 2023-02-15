CREATE TEMP TABLE DELETE_IDS AS
SELECT g."Id", g."CreatedDate" FROM public."Game" g
--WHERE g."CreatedDate" < now() - interval '1 month'
WHERE g."CreatedDate" < timestamp '2022-06-15 00:00:00';

--DELETE 
--SELECT *
FROM public."Game" g
WHERE g."Id" IN (SELECT "Id" FROM DELETE_IDS);

--DELETE
--SELECT *
FROM public."Team_Game" tg
WHERE tg."GameId" IN (SELECT "Id" FROM DELETE_IDS);