WITH cte1 (internal_card_id) AS 
(
	VALUES @usercards
),
cte2 AS (
SELECT
     --aggregated_results."Ruleset1",
     --aggregated_results."Ruleset2",
     --aggregated_results."ManaCap",
     aggregated_results."WinRate",
     aggregated_results."GamesPlayed",
     aggregated_results."TeamHash",
     aggregated_results."RatingBracket"
FROM public."@format_CachedTeams" aggregated_results 
WHERE
   aggregated_results."GamesPlayed" > @minGamesPlayed
   AND aggregated_results."Ruleset1" = '@ruleset1'
   AND aggregated_results."Ruleset2" = '@ruleset2'
   AND aggregated_results."RatingBracket" @ratingBracket -- special case
   AND aggregated_results."ManaCap" = @manaCap
   @additionalFilters
GROUP BY
   1,
   2,
   3,
   4)
SELECT cte2."WinRate",
		cte2."GamesPlayed",
		cte2."TeamHash",
		cte2."RatingBracket"
FROM cte2
WHERE cte2."TeamHash" NOT IN (
	SELECT
           tc."TeamHash" 
        FROM
           public."@format_Team_Card" tc 
        WHERE
			tc."TeamHash" IN (SELECT cte2."TeamHash" FROM CTE2)
           AND tc."CardId" NOT IN (SELECT cte1."internal_card_id" FROM CTE1)
   )
GROUP BY 1, 2, 3, 4;