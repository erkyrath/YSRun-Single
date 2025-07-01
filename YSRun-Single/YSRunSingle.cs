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

            Yarn.CompilerOutput compilerOutput = null;
            
            using (StreamReader infile = File.OpenText(gamefile)) {
                compilerOutput = jsonParser.Parse<Yarn.CompilerOutput>(infile);
            }

            var storage = new MemVariableStore();
            var dialogue = new Yarn.Dialogue(storage);
            var awaitinput = false;

            if (!startgame) {
                var joptions = new JsonSerializerOptions { WriteIndented = true };
                string json = File.ReadAllText("autosave.json");
                var autosave = JsonSerializer.Deserialize<AutosaveStruct>(json, joptions);
                storage = autosave.Storage;
            }

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

            if (true) {
                var autosave = new AutosaveStruct {
                    Storage = storage,
                };
                //### depretty
                var joptions = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize<AutosaveStruct>(autosave, joptions);
                File.WriteAllText("autosave.json", json+"\n");
            }
        }
    }

    internal struct AutosaveStruct
    {
        public MemVariableStore Storage { get; set; }
    }
}
