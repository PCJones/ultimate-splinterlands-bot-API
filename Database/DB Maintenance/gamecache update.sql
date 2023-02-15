--wild
INSERT INTO "wild_CachedTeams" ("Ruleset1", "Ruleset2", "ManaCap", "WinRate", "GamesPlayed", "TeamHash", "RatingBracket")
SELECT * FROM
    (
    SELECT team_results."Ruleset1",
            team_results."Ruleset2",
            team_results."ManaCap",
            avg(team_results."WinFlag") OVER (PARTITION BY team_results."TeamHash", team_results."Ruleset1", team_results."Ruleset2", team_results."ManaCap", team_results."RatingBracket") * 100 as "WinRate",
            count(team_results."TeamHash") OVER (PARTITION BY team_results."TeamHash", team_results."Ruleset1", team_results."Ruleset2", team_results."ManaCap", team_results."RatingBracket") as "GamesPlayed",
            team_results."TeamHash",
            team_results."RatingBracket"
            
    FROM 
        (SELECT g."Ruleset1",
            g."Ruleset2",
            g."ManaCap",
            CASE 
                 WHEN tg."Result" = 'W' THEN CAST(1 AS DECIMAL) 
                 WHEN tg."Result" = 'D' THEN CAST(0 AS DECIMAL)
                 ELSE CAST(0 AS DECIMAL) 
                 END as "WinFlag",
            tg."TeamHash",
             CASE
                 WHEN g."Rating" BETWEEN 0 AND 120 THEN 1
                 WHEN g."Rating" BETWEEN 121 AND 400 THEN 1
                 WHEN g."Rating" BETWEEN 401 AND 950 THEN 2
                 WHEN g."Rating" BETWEEN 951 AND 1400 THEN 3
                 WHEN g."Rating" BETWEEN 1401 AND 2050 THEN 4
                 ELSE 5
                 END as "RatingBracket"
            FROM public."wild_Game" g
            INNER JOIN public."wild_Team_Game" tg on tg."GameId" = g."Id") team_results
			-- if your DB is getting too big and the query times out then run it multiple times with different manacaps restrictions
			-- WHERE team_results."ManaCap" BETWEEN 10 AND 20
			-- WHERE team_results."ManaCap" BETWEEN 20 AND 30
			-- etc
            ) aggregated_results
WHERE "GamesPlayed" > 2
GROUP BY 1,2,3,4,5,6,7
ON CONFLICT ("Ruleset1", "Ruleset2", "ManaCap", "TeamHash", "RatingBracket") DO UPDATE SET "WinRate"=EXCLUDED."WinRate", "GamesPlayed" = excluded."GamesPlayed";

--modern
INSERT INTO "modern_CachedTeams" ("Ruleset1", "Ruleset2", "ManaCap", "WinRate", "GamesPlayed", "TeamHash", "RatingBracket")
SELECT * FROM
    (
    SELECT team_results."Ruleset1",
            team_results."Ruleset2",
            team_results."ManaCap",
            avg(team_results."WinFlag") OVER (PARTITION BY team_results."TeamHash", team_results."Ruleset1", team_results."Ruleset2", team_results."ManaCap", team_results."RatingBracket") * 100 as "WinRate",
            count(team_results."TeamHash") OVER (PARTITION BY team_results."TeamHash", team_results."Ruleset1", team_results."Ruleset2", team_results."ManaCap", team_results."RatingBracket") as "GamesPlayed",
            team_results."TeamHash",
            team_results."RatingBracket"
            
    FROM 
        (SELECT g."Ruleset1",
            g."Ruleset2",
            g."ManaCap",
            CASE 
                 WHEN tg."Result" = 'W' THEN CAST(1 AS DECIMAL) 
                 WHEN tg."Result" = 'D' THEN CAST(0 AS DECIMAL)
                 ELSE CAST(0 AS DECIMAL) 
                 END as "WinFlag",
            tg."TeamHash",
             CASE
                 WHEN g."Rating" BETWEEN 0 AND 120 THEN 1
                 WHEN g."Rating" BETWEEN 121 AND 400 THEN 1
                 WHEN g."Rating" BETWEEN 401 AND 950 THEN 2
                 WHEN g."Rating" BETWEEN 951 AND 1400 THEN 3
                 WHEN g."Rating" BETWEEN 1401 AND 2050 THEN 4
                 ELSE 5
                 END as "RatingBracket"
            FROM public."modern_Game" g
            INNER JOIN public."modern_Team_Game" tg on tg."GameId" = g."Id") team_results
			-- if your DB is getting too big and the query times out then run it multiple times with different manacaps restrictions
			-- WHERE team_results."ManaCap" BETWEEN 10 AND 20
			-- WHERE team_results."ManaCap" BETWEEN 20 AND 30
			-- etc
            ) aggregated_results
WHERE "GamesPlayed" > 2
GROUP BY 1,2,3,4,5,6,7
ON CONFLICT ("Ruleset1", "Ruleset2", "ManaCap", "TeamHash", "RatingBracket") DO UPDATE SET "WinRate"=EXCLUDED."WinRate", "GamesPlayed" = excluded."GamesPlayed";