using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Dapper;
using HistoryCrawler.Util;

namespace HistoryCrawler;

class Program
{
    static void Main(string[] args)
    {
        var appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder
        {

            Username = "",
            Password = "",
            Host = "",
            Database = "",
            Timeout = 1024,
            CommandTimeout = 1024,
            IncludeErrorDetail = true,
        };

        var connection1 = new Npgsql.NpgsqlConnection(connectionStringBuilder.ToString());
        var connection2 = new Npgsql.NpgsqlConnection(connectionStringBuilder.ToString());

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellationTokenSource.Token;

        Database database = new(connection1, connection2, cancellationToken);

        Crawler crawler = new(new(), database, cancellationToken);
        Task.Run(async () => await crawler.Loop()).ConfigureAwait(false);
        Task.Run(async () => await database.QueueLoop()).ConfigureAwait(false);

        string command = "";
        do
        {
            Helper.WriteLogLine("Enter command:");
            Helper.WriteLogLine("Enter command:");
            Helper.WriteLogLine("Enter command:");
            command = Console.ReadLine() ?? "";
        } while (command != "stop");
        cancellationTokenSource.Cancel();
        Helper.WriteLogLine("Stopping...");
        Console.ReadLine();
    }   
}