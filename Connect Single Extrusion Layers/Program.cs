using System;
using System.Numerics;
using Common;
using Utilities;
using static MyApp.ArgParser;
using Tools = Tools.Tools;

namespace MyApp // Note: actual namespace depends on the project name.
{
    internal class ArgParser
    {
        public readonly Mode WorkingMode;
        public readonly int VaseTransitionLayers;
        public readonly bool FilePathGiven;
        public readonly string FilePath;

        public enum Mode
        {
            Unknown = 0,
            Connect = 1,
            Vase = 2,
            AlignedVase = 3,
        }

        public ArgParser(string[] args)
        {
            Console.WriteLine("Args: ");
            for (var i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"{i}:{args[i]}");
            }
            Console.WriteLine();
            var argList = args.ToList();
            var givenFilePath =
                args.Length > 1 && File.Exists(args[1]) ? 1 :
                args.Length > 0 ? argList.FindIndex(x => File.Exists(x) && (x.EndsWith("gcode") || x.EndsWith("pp") || x.Contains("upload"))) : -1;
            var argConnect = argList.Any(x => x.ToLowerInvariant() == "connect");
            var argVase = argList.Any(x => x.ToLowerInvariant() == "vase");
            var argAlignedVase = argList.Any(x => x.ToLowerInvariant() == "aligned-vase");

            WorkingMode = argConnect ? Mode.Connect : argVase ? Mode.Vase : argAlignedVase ? Mode.AlignedVase : Mode.Unknown;
            VaseTransitionLayers = 4;

            FilePathGiven = givenFilePath >= 0;
            FilePath = FilePathGiven ? args[givenFilePath] : "C:\\Users\\burak\\Downloads\\Cable Winder Outer Shell_PLA_11m56s.gcode";
        }

        public string TargetPath(Mode mode)
        {
            return FilePathGiven ? FilePath : FilePath.Replace(".gcode", "_" + mode + ".gcode");
        }
    }

    internal class Program
    {
        public const bool VERBOSE = true;

        static void Main(string[] args)
        {
            var parsedArgs = new ArgParser(args);
            var mode = parsedArgs.WorkingMode;

            var fileController = new FileController(parsedArgs.FilePath);
            var lines = fileController.ReadAllLines();
            var parsed = new ParsedFile(lines);
            ConsoleFileDumper logDumper = parsedArgs.FilePathGiven ? new ConsoleFileDumper("C:\\Temp\\ConnectSingleExtrusionLayers.log") : null;
            Console.WriteLine(parsed.GetLayerInfo());

            //var reserialized = parsed.Parse();

            if (mode == Mode.Unknown)
            {
                Console.WriteLine("Select mode by typing mode number then hitting enter");
                Console.WriteLine("1: Connect");
                Console.WriteLine("2: Vase");
                Console.WriteLine("3: AlignedVase");
                var response = Console.ReadLine().ToLowerInvariant();
                if (int.TryParse(response, out var result))
                {
                    mode = (Mode)result;
                }
            }
            var fileWriteController = new FileController(parsedArgs.TargetPath(mode));

            Console.WriteLine("Starting with mode " + mode);

            var tools = new global::Tools.Tools(VERBOSE);

            switch (mode)
            {
                case Mode.Unknown:
                    break;
                case Mode.Connect:
                    {
                        Console.WriteLine("Connecting");
                        int count = tools.ConnectSingleExtrusionLayers(parsed.Layers);
                        Console.WriteLine($"Connected {count} layers!\nSaving");

                        fileWriteController.WriteAllLines(parsed.Parse());
                        Console.WriteLine("Saved!");
                    }
                    break;
                case Mode.Vase:
                    {
                        Console.WriteLine("Vase ");
                        int count = tools.VaseLayers(parsed.Layers, parsedArgs.VaseTransitionLayers);
                        Console.WriteLine($"Vase {count} layers with {parsedArgs.VaseTransitionLayers} transition layers!\nSaving");

                        fileWriteController.WriteAllLines(parsed.Parse());
                        Console.WriteLine("Saved!");
                    }
                    break;
                case Mode.AlignedVase:
                {
                    Console.WriteLine("Vase ");
                    int count = tools.VaseLayers(parsed.Layers, parsedArgs.VaseTransitionLayers, false, true);
                    Console.WriteLine($"Vase {count} layers with {parsedArgs.VaseTransitionLayers} transition layers!\nSaving");

                    fileWriteController.WriteAllLines(parsed.Parse());
                    Console.WriteLine("Saved!");
                }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            logDumper?.Dispose();
        }
    }
}