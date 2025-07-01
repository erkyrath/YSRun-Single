using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YSRunSingle
{
    #nullable enable
    
    public class YSRunSingle
    {
        public static int Main(string[] args)
        {
            bool start = false;
            string? gamefile = null;
            
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

            Yarn.CompilerOutput? compilerOutput = null;
            
            using (StreamReader infile = File.OpenText(gamefile)) {
                compilerOutput = jsonParser.Parse<Yarn.CompilerOutput>(infile);
            }

            RunState runstate = new RunState();
            MemVariableStore storage = new MemVariableStore();
            Yarn.Dialogue dialogue = new Yarn.Dialogue(storage);

            dialogue.LogErrorMessage = (val) => Console.Error.WriteLine(val);
            dialogue.SetProgram(compilerOutput.Program);

            if (!startgame) {
                var joptions = new JsonReaderOptions { };
                string json = File.ReadAllText("autosave.json");
                runstate.JsonReadAutosave(dialogue, json, joptions);
                //###
                if (true) {
                    var jwoptions = new JsonWriterOptions { Indented = true };
                    string json2 = runstate.JsonWriteAutosave(dialogue, jwoptions);
                    File.WriteAllText("autosave-copy.json", json2+"\n");
                }
                //###
            }

            var awaitinput = false;
            
            if (startgame) {
                dialogue.SetNode("Start");
            }
            else {
                dialogue.SetSelectedOption(0); //###
            }

            string TextForLine(string lineID, string[] subs)
            {
                string text = compilerOutput.Strings[lineID].Text;
                return String.Format(text, subs);
            }

            void LineHandler(Yarn.Line line)
            {
                Console.WriteLine(TextForLine(line.ID, line.Substitutions));
            }

            void OptionsHandler(Yarn.OptionSet options)
            {
                int count = 0;
                foreach (var option in options.Options) {
                    var availstr = option.IsAvailable ? "" : " (unavailable)";
                    Console.WriteLine($"{count}:{availstr} {TextForLine(option.Line.ID, option.Line.Substitutions)}");
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
                //### depretty
                var joptions = new JsonWriterOptions { Indented = true };
                string json = runstate.JsonWriteAutosave(dialogue, joptions);
                File.WriteAllText("autosave.json", json+"\n");
            }
        }
    }

    internal struct RunState
    {
        public void JsonReadAutosave(Yarn.Dialogue dialogue, string json, JsonReaderOptions joptions)
        {
            var options = new JsonSerializerOptions { };
            
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(bytes, joptions);
            
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject) {
                throw new JsonException();
            }
            while (reader.Read()) {
                if (reader.TokenType == JsonTokenType.EndObject) {
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName) {
                    throw new JsonException();
                }

                string? propName = reader.GetString();
                reader.Read();
                
                if (propName == "Storage") {
                    var storageconv = new MemVariableStoreConverter();
                    var storage = storageconv.Read(ref reader, typeof(MemVariableStore), options);
                    dialogue.SetStorage(storage);
                }
                else if (propName == "State") {
                    dialogue.JsonReadState(ref reader, options);
                }
                else {
                    throw new JsonException();
                }
                
            }
        }
        
        public string JsonWriteAutosave(Yarn.Dialogue dialogue, JsonWriterOptions joptions)
        {
            MemVariableStore? storage = dialogue.VariableStorage as MemVariableStore;
            if (storage == null) {
                throw new Exception("VariableStorage is not jsonable");
            }
            
            var options = new JsonSerializerOptions { WriteIndented = joptions.Indented };
            
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, joptions);

            writer.WriteStartObject();
            
            var storageconv = new MemVariableStoreConverter();
            writer.WritePropertyName("Storage");
            storageconv.Write(writer, storage, options);
            
            writer.WritePropertyName("State");
            dialogue.JsonWriteState(writer, options);
                
            writer.WriteEndObject();

            writer.Flush();
            string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return json;
        }
    };
}
