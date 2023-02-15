-- Get all the affected IDs into a tmp table
SELECT agg_res."Id"

INTO TEMP TABLE TMP_TABLE_IDS

FROM public."CachedTeams" agg_res
INNER JOIN public."Team_Card" tc ON tc."TeamHash" = agg_res."TeamHash"
WHERE agg_res."ManaCap" >= 15
GROUP BY 1
HAVING COUNT(agg_res."TeamHash") = 2;

-- Run the delete for CachedTeams table using the tmp table IDs
-- SELECT * 
DELETE
FROM public."CachedTeams" agg_res
WHERE agg_res."Id" IN( 
    SELECT * FROM TMP_TABLE_IDS);
