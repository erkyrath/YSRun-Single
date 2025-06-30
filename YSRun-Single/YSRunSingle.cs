using System;
using System.IO;

namespace YSRunSingle
{
    using System;

    public class YSRunSingle
    {
        public static int Main(string[] args)
        {
            bool start = false;
            string gamefile = null;
            
            foreach (var val in args) {
                if (val == "--start") {
                    start = true;
                    continue;
                }
                gamefile = val;
            }
            if (gamefile == null) {
                Console.WriteLine("You must supply a game filename");
                return 1;
            }
            try {
                RunTurn(gamefile, start);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"{ex.Message}");
                return 1;
            }
            return 0;
        }

        public static void RunTurn(string gamefile, bool startgame)
        {
            Console.WriteLine($"### running {gamefile}, start={startgame}");

            var settings = new Google.Protobuf.JsonParser.Settings(8);
            var jsonParser = new Google.Protobuf.JsonParser(settings);

            StreamReader infile = File.OpenText(gamefile);
            var compilerOutput = jsonParser.Parse<Yarn.CompilerOutput>(infile);
            infile.Dispose();

            
        }
    }
}
