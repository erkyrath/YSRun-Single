using System;
using System.IO;
using System.Text.Json;

namespace YSRunSingle
{
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
            var settings = new Google.Protobuf.JsonParser.Settings(8);
            var jsonParser = new Google.Protobuf.JsonParser(settings);

            StreamReader infile = File.OpenText(gamefile);
            var compilerOutput = jsonParser.Parse<Yarn.CompilerOutput>(infile);
            infile.Dispose();

            var storage = new MemVariableStore();
            var dialogue = new Yarn.Dialogue(storage);
            var awaitinput = false;

            dialogue.LogErrorMessage = (val) => Console.Error.WriteLine(val);
            dialogue.SetProgram(compilerOutput.Program);
            dialogue.SetNode("Start");

            string TextForLine(string lineID)
            {
                return compilerOutput.Strings[lineID].Text;
            }

            void LineHandler(Yarn.Line line)
            {
                Console.WriteLine(TextForLine(line.ID));
            }

            void OptionsHandler(Yarn.OptionSet options)
            {
                int count = 0;
                foreach (var option in options.Options) {
                    var availstr = option.IsAvailable ? "" : " (unavailable)";
                    Console.WriteLine($"{count}:{availstr} {TextForLine(option.Line.ID)}");
                    count += 1;
                }
                awaitinput = true;
            }

            dialogue.LineHandler = LineHandler;
            dialogue.OptionsHandler = OptionsHandler;

            do {
                dialogue.Continue();
            }
            while (dialogue.IsActive && !awaitinput);

            //### depretty
            var joptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(storage, joptions);
            File.WriteAllText("autosave.json", json);
        }
    }
}
