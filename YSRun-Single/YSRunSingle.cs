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
                Console.WriteLine("### No gamefile");
                return 1;
            }
            RunTurn(gamefile, start);
            return 0;
        }

        public static void RunTurn(string gamefile, bool startgame)
        {
            Console.WriteLine($"### running {gamefile}, start={startgame}");
        }
    }
}
