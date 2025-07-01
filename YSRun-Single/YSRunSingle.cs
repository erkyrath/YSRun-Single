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

            MemVariableStore storage;
            Yarn.Dialogue dialogue;

            if (startgame) {
                storage = new MemVariableStore();
                dialogue = new Yarn.Dialogue(storage);
            }
            else {
                var joptions = new JsonSerializerOptions { WriteIndented = true };
                string json = File.ReadAllText("autosave.json");
                var autosave = JsonSerializer.Deserialize<AutosaveStruct>(json, joptions);
                storage = autosave.Storage;
                dialogue = autosave.Dialogue;
            }

            var awaitinput = false;
            
            dialogue.LogErrorMessage = (val) => Console.Error.WriteLine(val);
            dialogue.SetProgram(compilerOutput.Program);

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
                var autosave = new AutosaveStruct {
                    Storage = storage,
                    Dialogue = dialogue,
                };
                //### depretty
                var joptions = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize<AutosaveStruct>(autosave, joptions);
                File.WriteAllText("autosave.json", json+"\n");
            }
        }
    }

    [JsonConverter(typeof(AutosaveStructConverter))]
    internal struct AutosaveStruct
    {
        internal MemVariableStore Storage;
        internal Yarn.Dialogue Dialogue;
    }

    internal class AutosaveStructConverter : JsonConverter<AutosaveStruct>
    {
        public override AutosaveStruct Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) {
            var autosave = new AutosaveStruct();

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
                    autosave.Storage = storageconv.Read(ref reader, typeof(MemVariableStore), options);
                }
                else if (propName == "State") {
                    if (autosave.Storage == null) {
                        throw new JsonException();
                    }
                    autosave.Dialogue = new Yarn.Dialogue(autosave.Storage);
                    autosave.Dialogue.JsonReadState(ref reader, options);
                }
                else {
                    throw new JsonException();
                }
                
            }
            
            return autosave;
        }
        
        public override void Write(
            Utf8JsonWriter writer,
            AutosaveStruct autosave,
            JsonSerializerOptions options) {
            writer.WriteStartObject();
            if (autosave.Storage != null) {
                var storageconv = new MemVariableStoreConverter();
                writer.WritePropertyName("Storage");
                storageconv.Write(writer, autosave.Storage, options);
            }
            if (autosave.Dialogue != null) {
                writer.WritePropertyName("State");
                autosave.Dialogue.JsonWriteState(writer, options);
            }
            writer.WriteEndObject();
        }
    }
}
