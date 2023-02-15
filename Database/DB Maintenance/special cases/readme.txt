delete_cleanup games.sql:
delete oldest games for a specific ruleset,mana,ratingbracket combination if more than n(default: 40.000) games are crawled for it.
It will delete the oldest games so that there are exactly n(default: 40.000) games after running the query

delete 2 mana games.sql:
Deletes 2 mana games from cachedteams, not from raw Game table
probably not needed anymore

delete games older than.sql:
Self explanatory

delete non spellbook users.sql:
self explanatory