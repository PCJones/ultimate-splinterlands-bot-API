# ultimate-splinterlands-bot-API
Team generating API for [Ultimate Splinterlands Bot V2](https://github.com/PCJones/Ultimate-Splinterlands-Bot-V2)

The source code is quite a mess as I hacked the API together in a day or so, and then had to update it often without really having the time - sorry in advance!

Any help in development is appreciated, make sure to join the [Discord server](https://discord.gg/hwSr7KNGs9).
If you need help in setting up the project you can also feel free to ask for help on the discord server :)

## Understanding the code
Until there is better documentation, it's probably best to set a breakpoint at the beginning of the `TeamGeneration.GetTeamAsync()` method and then to debug trough the whole method.
I've tried to add comments where I could but it's really quite a mess, sorry again :D

## Database
The database in use is PostgreSQL

## Game History Crawler
```
Setup Database connection: Program.cs
Setup Proxy servers: WebRequest.cs (it's coded for socks 5 servers but can work with almost all with minimal code changes)
I've also added a quick workaround so that the crawler works without proxys
```

## Changing rating brackets
Rating bracket has to be changed in these places:
```
API: WebApi.GetRatingBracket()
SQL Query: gamecache update.sql
```


# Special thanks
A huge thanks to @chaylins and @0xh3m4n7 for helping with the more complex SQL queries!
