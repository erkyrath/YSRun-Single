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
            string autodir = ".";

            for (int ix=0; ix<args.Length; ix++) {
                var val = args[ix];
                if (val == "--help") {
                    Console.WriteLine("usage: ysrun [ --start ] [ --autodir DIR ] GAME.yarn.json");
                    return 1;
                }
                if (val == "--start") {
                    start = true;
                    continue;
                }
                if (val == "--autodir") {
                    ix++;
                    autodir = args[ix];
                    continue;
                }
                gamefile = val;
            }
            if (gamefile == null) {
                Console.WriteLine("You must supply a game filename");
                return 1;
            }

            var runner = new YSRunSingle {
                autosavepath = Path.Combine(autodir, "autosave.json")
            };
            
            try {
                runner.ReadGameFile(gamefile);
                var input = runner.ReadStanza();
                runner.RunTurn(input, start);
                runner.GenerateOutput();
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"{ex.Message}");
                return 1;
            }
            return 0;
        }

        string? autosavepath;
        
        // The game file data.
        Yarn.CompilerOutput? compilerOutput = null;

        // The Yarn dialogue structure.
        Yarn.Dialogue? dialogue = null;
        
        // Execution state for our turn. (The boundary between YSRunner and
        // RunState is a bit fuzzy; this was originally modelled on the
        // Ink runner, which is Javascript.)
        RunState runstate = new RunState();

        // Read one JSON stanza from stdin. The stanza must be terminated
        // by a newline, but it doesn't have to arrive all one one line.
        //
        // This is pretty bad at detecting non-JSON input. If the input
        // is garbage, it will keep reading forever hoping that the
        // next line will make it valid. Of course if the first line
        // doesn't start with "{", it will never be valid, but we don't
        // detect that case.
        public JsonDocument ReadStanza()
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

        // Read the game file data from a JSON ("machine-readable")
        // file.
        // This sets both compilerOutput and dialogue. We will need
        // both.
        internal void ReadGameFile(string gamefile)
        {
            var settings = new Google.Protobuf.JsonParser.Settings(8);
            var jsonParser = new Google.Protobuf.JsonParser(settings);

            using (StreamReader infile = File.OpenText(gamefile)) {
                compilerOutput = jsonParser.Parse<Yarn.CompilerOutput>(infile);
            }

            MemVariableStore storage = new MemVariableStore();
            dialogue = new Yarn.Dialogue(storage);

            dialogue.LogErrorMessage = (val) => Console.Error.WriteLine(val);
            dialogue.LineHandler = LineHandler;
            dialogue.OptionsHandler = OptionsHandler;
            
            dialogue.SetProgram(compilerOutput.Program);
        }

        // Perform the turn.
        // If startgame is true, the input must be a GlkOte init
        // object. We will run the game up to the first choice.
        // If startgame is false, the input must be a GlkOte hyperlink
        // event. We will accept that choice and run the game to the
        // next choice.
        internal void RunTurn(JsonDocument input, bool startgame)
        {
            if (dialogue == null || compilerOutput == null) {
                throw new Exception("game not loaded");
            }

            // If this isn't the first turn, load the autosave.
            if (!startgame) {
                var joptions = new JsonReaderOptions { };
                string json = File.ReadAllText(autosavepath!);
                runstate.JsonReadAutosave(dialogue, json, joptions);
            }

            int selectedoption = -1;

            // Deal with the input object.
            if (startgame) {
                JsonElement metricsel;
                if (!input.RootElement.TryGetProperty("metrics", out metricsel)) {
                    throw new Exception("first input had no metrics");
                }
                JsonElement el;
                if (metricsel.TryGetProperty("width", out el)) {
                    runstate.metrics_width = el.GetSingle();
                }
                if (metricsel.TryGetProperty("height", out el)) {
                    runstate.metrics_height = el.GetSingle();
                }
                runstate.has_metrics = true;
                runstate.newinput = true;
                runstate.newturn = true;
            }
            else {
                string? intype = null;
                string? invalue = null;
                int inwin = 0;
                JsonElement el;
                if (input.RootElement.TryGetProperty("type", out el)) {
                    intype = el.GetString();
                }
                if (input.RootElement.TryGetProperty("window", out el)) {
                    inwin = el.GetInt32();
                }
                if (input.RootElement.TryGetProperty("value", out el)) {
                    invalue = el.GetString();
                }
                if (intype == "hyperlink" && inwin == 1 && invalue != null) {
                    runstate.newinput = true;
                    var ls = invalue.Split(':');
                    if (ls.Length == 2) {
                        int valturn, valindex;
                        string? valtext = null;
                        if (Int32.TryParse(ls[0], out valturn) && Int32.TryParse(ls[1], out valindex)) {
                            if (valturn == runstate.game_turn && runstate.OptionIsValid(valindex, out valtext)) {
                                runstate.newturn = true;
                                runstate.choicetext = valtext;
                                selectedoption = valindex;
                            }
                        }
                    }
                }
            }

            // We only crank the engine if we have a valid choice. (Or if the game is being inited.)
            if (startgame || selectedoption >= 0) {
                if (startgame) {
                    dialogue.SetNode("Start");
                }
                else {
                    dialogue.SetSelectedOption(selectedoption);
                }

                // Clear last turn's options.
                runstate.outoptions.Clear();

                do {
                    dialogue.Continue();
                }
                while (dialogue.IsActive && !dialogue.IsAwaitingOptions);

                if (!dialogue.IsAwaitingOptions || runstate.outoptions.Count == 0) {
                    runstate.storydone = true;
                }
            }
            
            runstate.gen++;
            if (runstate.newturn) {
                runstate.game_turn++;
            }

            // We always autosave at the end of the turn. (Even if the engine
            // was not cranked, we need to save the new runstate.gen.)
            if (true) {
                var joptions = new JsonWriterOptions { Indented = false };
                string json = runstate.JsonWriteAutosave(dialogue, joptions);
                File.WriteAllText(autosavepath!, json+"\n");
            }
        }

        // Dialogue line output handler.
        internal void LineHandler(Yarn.Line line)
        {
            var text = TextForLine(line.ID, line.Substitutions);
            runstate.outlines.Add(text);
        }

        // Dialogue options presentation handler.
        internal void OptionsHandler(Yarn.OptionSet options)
        {
            int optnum = 0;
            foreach (var option in options.Options) {
                if (option.IsAvailable) {
                    var text = TextForLine(option.Line.ID, option.Line.Substitutions);
                    var opt = new OutOption { OptNum=optnum, Text=text };
                    runstate.outoptions.Add(opt);
                }
                optnum++;
            }
        }

        // Figure out the substituted text for a Yarn line.
        internal string TextForLine(string lineID, string[] subs)
        {
            string text = compilerOutput!.Strings[lineID].Text;
            return String.Format(text, subs);
        }

        // Generate the GlkOte output of our turn.
        internal void GenerateOutput()
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
    
                foreach (var opt in runstate.outoptions) {
                    var dat = new JsonObject {
                        ["content"] = new JsonArray(
                            new JsonObject {
                                ["style"] = "note",
                                ["text"] = opt.Text,
                                ["hyperlink"] = $"{runstate.game_turn}:{opt.OptNum}",
                            }
                        )
                    };
                    contentlines.Add(dat);
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

    internal struct OutOption
    {
        public int OptNum { get; set; }
        public string Text { get; set; }
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
        public List<OutOption> outoptions = new List<OutOption>();

        // Check whether a given option number is a current valid
        // choice. If so, return its text.
        // (Option numbers are as passed to SetSelectedOption(). They
        // start at zero but may not be sequential.
        public bool OptionIsValid(int val, out string? text)
        {
            foreach (var opt in outoptions) {
                if (opt.OptNum == val) {
                    text = opt.Text;
                    return true;
                }
            }
            text = null;
            return false;
        }
        
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
                else if (propName == "MetricsHeight") {
                    metrics_height = reader.GetSingle();
                    has_metrics = true;
                }
                else if (propName == "OutOptions") {
                    var ls = JsonSerializer.Deserialize<List<OutOption>>(ref reader, options);
                    if (ls != null)
                        outoptions = ls;
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

            // Yes, we save outoptions even though the same information
            // is in vm.currentOptions. It's convenient.
            if (outoptions.Count > 0) {
                writer.WritePropertyName("OutOptions");
                JsonSerializer.Serialize<List<OutOption>>(writer, outoptions, options);
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
