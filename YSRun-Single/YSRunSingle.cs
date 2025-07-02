using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                var input = HandleInput();
                RunTurn(gamefile, start);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"{ex.Message}");
                return 1;
            }
            return 0;
        }

        public static JsonDocument HandleInput()
        {
            Stream stream = Console.OpenStandardInput();
            // I'm sure there's an efficient way to do this, but I'm using a List.
            byte[] bytes = new byte[256];
            List<byte> buf = new List<byte>();
            while (true) {
                int len = stream.Read(bytes, 0, 256);
                if (len == 0) {
                    throw new Exception("end of input and not JSON");
                }
                int pos = 0;
                while (pos < len) {
                    int ix;
                    for (ix=pos; ix<len; ix++) {
                        if (bytes[ix] == 10) {
                            ix++;
                            break;
                        }
                    }
                    buf.AddRange(bytes[ pos .. ix ]);
                    pos = ix;
                    
                    try {
                        var obj = JsonDocument.Parse(buf.ToArray());
                        return obj;
                    }
                    catch (JsonException) {
                        continue;
                    }
                }
            }
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
            }

            //###
            if (startgame) {
                runstate.newturn = true;
                runstate.newinput = true;
                runstate.metrics_width = 80;
                runstate.metrics_height = 24;
                runstate.has_metrics = true;
            }
            //###

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
                var text = TextForLine(line.ID, line.Substitutions);
                runstate.outlines.Add(text);
            }

            void OptionsHandler(Yarn.OptionSet options)
            {
                foreach (var option in options.Options) {
                    if (option.IsAvailable) {
                        var text = TextForLine(option.Line.ID, option.Line.Substitutions);
                        runstate.outoptions.Add(text);
                    }
                }
                awaitinput = true;
            }

            dialogue.LineHandler = LineHandler;
            dialogue.OptionsHandler = OptionsHandler;

            do {
                dialogue.Continue();
            }
            while (dialogue.IsActive && !awaitinput);

            if (!awaitinput || runstate.outoptions.Count == 0) {
                runstate.storydone = true;
            }

            if (true) {
                //### depretty
                var joptions = new JsonWriterOptions { Indented = true };
                string json = runstate.JsonWriteAutosave(dialogue, joptions);
                File.WriteAllText("autosave.json", json+"\n");
            }

            GenerateOutput(dialogue, runstate);
        }

        internal static void GenerateOutput(Yarn.Dialogue dialogue, RunState runstate)
        {
            var output = new JsonObject {
                ["type"] = "update",
                ["gen"] = runstate.gen,
            };

            if (runstate.gen <= 1) {
                output["windows"] = new JsonArray(
                    new JsonObject {
                        ["id"] = 1,
                        ["type"] = "buffer",
                        ["rock"] = 0,
                        ["left"] = 0,
                        ["top"] = 0,
                        ["width"] = runstate.metrics_width,
                        ["height"] = runstate.metrics_height,
                    }
                );
            }
            
            var contentlines = new JsonArray();

            if (runstate.newturn) {
                if (runstate.choicetext != null) {
                    var dat = new JsonObject {
                        ["content"] = new JsonArray(
                            new JsonObject {
                                ["style"] = "input",
                                ["text"] = runstate.choicetext,
                            }
                        )
                    };
                    contentlines.Add(dat);
                }
                
                foreach (var text in runstate.outlines) {
                    var dat = new JsonObject {
                        ["content"] = new JsonArray(
                            new JsonObject {
                                ["style"] = "normal",
                                ["text"] = text,
                            }
                        )
                    };
                    contentlines.Add(dat);
                }
    
                int optcount = 0;
                foreach (var text in runstate.outoptions) {
                    var dat = new JsonObject {
                        ["content"] = new JsonArray(
                            new JsonObject {
                                ["style"] = "note",
                                ["text"] = text,
                                ["hyperlink"] = $"{runstate.game_turn}:{optcount}",
                            }
                        )
                    };
                    contentlines.Add(dat);
                    optcount++;
                }

            }

            if (runstate.newinput) {
                if (runstate.outoptions.Count > 0) {
                    output["input"] = new JsonArray(new JsonObject {
                        ["id"] = 1,
                        ["gen"] = 0,
                        ["hyperlink"] = true,
                    });
                }
            }

            if (contentlines.Count > 0) {
                output["content"] = new JsonArray(new JsonObject {
                    ["id"] = 1,
                    ["text"] = contentlines
                });
            }

            if (runstate.storydone) {
                output["exit"] = true;
            }

            var options = new JsonSerializerOptions { WriteIndented = false };
            Console.WriteLine(output.ToJsonString(options));
        }
    }

    internal struct RunState
    {
        public RunState() {}
        
        public int gen = 0;
        public int game_turn = 0;
        public bool has_metrics = false;
        public float metrics_width = 0;
        public float metrics_height = 0;
        public bool newinput = false;
        public bool newturn = false;
        public bool storydone = false;
        public string? choicetext = null;
        public List<string> outlines = new List<string>();
        public List<string> outoptions = new List<string>();
        
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
                
                if (propName == "Turn") {
                    game_turn = reader.GetInt32();
                }
                else if (propName == "Gen") {
                    gen = reader.GetInt32();
                }
                else if (propName == "MetricsWidth") {
                    metrics_width = reader.GetSingle();
                    has_metrics = true;
                }
                else if (propName == "Metricsheight") {
                    metrics_height = reader.GetSingle();
                    has_metrics = true;
                }
                else if (propName == "Storage") {
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

            writer.WriteNumber("Turn", game_turn);
            writer.WriteNumber("Gen", gen);

            if (has_metrics) {
                writer.WriteNumber("MetricsWidth", metrics_width);
                writer.WriteNumber("MetricsHeight", metrics_height);
            }
            
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
