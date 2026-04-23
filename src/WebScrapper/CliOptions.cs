namespace WebScrapper;

public sealed class CliOptions
{
    public List<string> Seeds { get; } = new();
    public List<string> SearchQueries { get; } = new();
    public int SearchMaxResults { get; set; } = 25;
    public int Threshold { get; set; } = 30;
    public int Concurrency { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 15;
    public string OutputFile { get; set; } = "output/leads.txt";
    public string DatabasePath { get; set; } = "data/scraper.db";
    public bool ShowHelp { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    o.ShowHelp = true;
                    break;
                case "--seeds":
                    o.Seeds.Add(RequireValue(args, ref i));
                    break;
                case "--search":
                    o.SearchQueries.Add(RequireValue(args, ref i));
                    break;
                case "--search-max":
                    o.SearchMaxResults = int.Parse(RequireValue(args, ref i));
                    break;
                case "--threshold":
                    o.Threshold = int.Parse(RequireValue(args, ref i));
                    break;
                case "--concurrency":
                    o.Concurrency = Math.Max(1, int.Parse(RequireValue(args, ref i)));
                    break;
                case "--timeout":
                    o.TimeoutSeconds = Math.Max(1, int.Parse(RequireValue(args, ref i)));
                    break;
                case "--output":
                    o.OutputFile = RequireValue(args, ref i);
                    break;
                case "--db":
                    o.DatabasePath = RequireValue(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }
        return o;
    }

    private static string RequireValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {args[i]}");
        return args[++i];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            WebScrapper - find poorly-built company websites.

            Usage:
              WebScrapper [options]

            Source options (provide at least one):
              --seeds <file>         Text file of candidate URLs (one per line, # = comment).
                                     May be passed multiple times.
              --search "<query>"     Discover candidates via DuckDuckGo HTML search.
                                     May be passed multiple times.
              --search-max <n>       Max results per search query. Default: 25.

            Scoring / behavior:
              --threshold <n>        Minimum score to treat as a lead. Default: 30.
              --concurrency <n>      Parallel HTTP workers. Default: 8.
              --timeout <sec>        Per-request timeout. Default: 15.

            Output:
              --output <file>        Lead output file. Default: output/leads.txt.
              --db <file>            SQLite database path. Default: data/scraper.db.

              -h, --help             Show this help.

            Examples:
              WebScrapper --seeds seeds.example.txt
              WebScrapper --search "plumber cape town" --search "electrician johannesburg" --threshold 35
              WebScrapper --seeds my-prospects.txt --concurrency 16 --output leads/today.txt
            """);
    }
}
