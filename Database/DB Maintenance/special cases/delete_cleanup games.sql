CREATE TEMP TABLE DELETE_IDS AS
SELECT sub_select."Id" 
    FROM(
        SELECT main_select."Id",
            main_select."Ruleset1",
            main_select."Ruleset2", 
            main_select."RatingBracket",
            main_select."CreatedDate",
            ROW_NUMBER () OVER (
                                PARTITION BY main_select."Ruleset1", main_select."Ruleset2", main_select."RatingBracket"
                                ORDER BY main_select."CreatedDate" DESC)
        FROM 
        (
        SELECT 
                    g."Id",
                    g."Ruleset1",
                    g."Ruleset2",
                     CASE
                         WHEN g."Rating" BETWEEN 0 AND 120 THEN 1
                         WHEN g."Rating" BETWEEN 121 AND 400 THEN 1
                         WHEN g."Rating" BETWEEN 401 AND 950 THEN 2
                         WHEN g."Rating" BETWEEN 951 AND 1400 THEN 3
                         WHEN g."Rating" BETWEEN 1401 AND 2050 THEN 4
                         ELSE 5
                         END AS "RatingBracket",
                    g."CreatedDate"
                    --FROM public."modern_Game" g
                    FROM public."wild_Game" g
					-- SPECIFY ruleset if your DB is too big and run the query multiple times
                    -- WHERE g."Ruleset1" = 'Melee Mayhem'
            ) main_select
    ) sub_select
WHERE ROW_NUMBER > 40000;

DELETE 
--SELECT *
--FROM public."modern_Game" g
FROM public."wild_Game" g
WHERE g."Id" IN (SELECT "Id" FROM DELETE_IDS);
DELETE
--SELECT *
--FROM public."modern_Team_Game" tg
FROM public."wild_Team_Game" tg
WHERE tg."GameId" IN (SELECT "Id" FROM DELETE_IDS);