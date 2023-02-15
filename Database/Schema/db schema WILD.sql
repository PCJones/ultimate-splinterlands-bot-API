BEGIN TRANSACTION;
--DROP TABLE IF EXISTS "wild_Game" CASCADE;
CREATE TABLE IF NOT EXISTS "wild_Game" (
	"Id"	serial primary key,
	"QueueIdHash"	TEXT NOT NULL,
	"QueueId1"	TEXT NOT NULL,
	"Winner"	TEXT NOT NULL,
	"Loser"		TEXT NOT NULL,
	"Rating"	INTEGER NOT NULL,
	"CreatedDate"	TIMESTAMP NOT NULL,
	"MatchType"	TEXT NOT NULL,
	"ManaCap"	INTEGER NOT NULL CHECK("ManaCap" <= 100),
	"Ruleset1"	TEXT NOT NULL,
	"Ruleset2"	TEXT NOT NULL,
	"Inactive"	TEXT NOT NULL,
	"TournamentSettings"	TEXT,
	unique("QueueIdHash", "QueueId1")  
	
);

--DROP TABLE IF EXISTS "wild_Team_Card" CASCADE;
CREATE TABLE IF NOT EXISTS "wild_Team_Card" (
	"TeamHash"	BIGINT NOT NULL,
    "CardId"    INTEGER NOT NULL,
    "Position"  INTEGER NOT NULL,
    FOREIGN KEY("CardId") REFERENCES "Card"("Id"),
	PRIMARY KEY("TeamHash", "CardId", "Position"));
CREATE INDEX ON public."wild_Team_Card" ("TeamHash");
CREATE INDEX ON public."wild_Team_Card" ("CardId");

--DROP TABLE IF EXISTS "wild_Team_Game" CASCADE;
CREATE TABLE IF NOT EXISTS "wild_Team_Game" (
    "GameId"    INTEGER NOT NULL,
    "TeamHash"    BIGINT NOT NULL,
    "Result" TEXT NOT NULL,
	UNIQUE("GameId", "Result", "TeamHash"),
    FOREIGN KEY("GameId") REFERENCES "wild_Game"("Id"),
    --FOREIGN KEY("TeamHash") REFERENCES "wild_Team_Card"("TeamHash"),
	PRIMARY KEY("GameId", "Result", "TeamHash")
    --PRIMARY KEY("GameId", "Result", "TeamHash")
);


-- Not relevant for crawler game inserts
--DROP TABLE IF EXISTS "wild_CachedTeams";
CREATE TABLE IF NOT EXISTS "wild_CachedTeams" (
	"Id"	serial primary key,
	"Ruleset1"	TEXT NOT NULL,
	"Ruleset2"	TEXT NOT NULL,
	"ManaCap"	INTEGER NOT NULL,
	"WinRate"	numeric NOT NULL,
    "GamesPlayed"    BIGINT NOT NULL,
    "TeamHash"  BIGINT NOT NULL,
    "RatingBracket"  INTEGER NOT NULL,
	UNIQUE("Ruleset1", "Ruleset2", "ManaCap", "TeamHash", "RatingBracket"));
CREATE INDEX ON public."wild_CachedTeams" ("Ruleset1");
CREATE INDEX ON public."wild_CachedTeams" ("TeamHash");
CREATE INDEX ON public."wild_CachedTeams" ("RatingBracket");

COMMIT;
