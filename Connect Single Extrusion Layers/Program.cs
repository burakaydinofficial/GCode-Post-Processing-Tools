using System;
using System.Numerics;
using Common;
using Utilities;

namespace MyApp // Note: actual namespace depends on the project name.
{
    internal class Program
    {
        public const bool VERBOSE = true;

        static void Main(string[] args)
        {
            Console.WriteLine("Args: ");
            for (var i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"{i}:{args[i]}");
            }
            Console.WriteLine();
            var givenFilePath = 
                args.Length > 1 && File.Exists(args[1]) ? 1 : 
                args.Length > 0 && File.Exists(args[0]) && (args[0].EndsWith("gcode") || args[0].EndsWith("pp") || args[0].Contains("upload")) ? 0 : -1;
            var filePathGiven = givenFilePath >= 0;
            var filePath = filePathGiven ? args[givenFilePath] : "C:\\Users\\burak\\Downloads\\Cable Winder Outer Shell_PLA_11m56s.gcode";
            var targetPath = filePathGiven ? filePath : filePath.Replace(".gcode", "_connected.gcode");
            var fileController = new FileController(filePath);
            var lines = fileController.ReadAllLines();
            var parsed = new ParsedFile(lines);
            ConsoleFileDumper logDumper = filePathGiven ? new ConsoleFileDumper("C:\\Temp\\ConnectSingleExtrusionLayers.log") : null;
            Console.WriteLine(parsed.GetLayerInfo());
            var reserialized = parsed.Parse();
            if (!filePathGiven)
                Console.WriteLine("In order to connect layers type connect and hit enter");
            var response = filePathGiven || Console.ReadLine() == "connect";
            if (response)
            {
                Console.WriteLine("Connecting");
                int count = ConnectSingleExtrusionLayers(parsed.Layers);
                Console.WriteLine($"Connected {count} layers!\nSaving");
                var fileWriteController = new FileController(targetPath);
                fileWriteController.WriteAllLines(parsed.Parse());
                Console.WriteLine("Saved!");
                if (!filePathGiven)
                    Console.ReadLine();
            }
            logDumper?.Dispose();
        }

        public static int ConnectSingleExtrusionLayers(IReadOnlyList<Layer> layers)
        {
            int count = 0;
            for (int i = 0; i < layers.Count - 2; i++)
            {
                var a = layers[i];
                var b = layers[i + 1];
                if (ConnectSingleExtrusionLayers(a, b, true, 2f, 2f))
                    count++;
            }
            return count;
        }

        public static bool ConnectSingleExtrusionLayers(Layer a, Layer b, bool relativeExtrusion, float maxConnectionDistance = 2f, float feedRateMultiplier = 0.8f)
        {
            if (!a.IsSingleExtrusion() || !b.IsSingleExtrusion())
            {
                return false;
            }

            if (a.LayerZ < 8)
            {
                feedRateMultiplier = a.LayerZ * 0.5f;
            }

            var aCommands = a.GetAllMoveCommands();
            var bCommands = b.GetAllMoveCommands();
            var aLast = aCommands[^1];
            var aLast2 = aCommands[^2];
            var dist = MoveCommand.GetDistance(aLast2, aLast);
            var feedRate = (relativeExtrusion ? aLast.E.Value / dist : (aLast2.E.Value - aLast.E.Value) / dist) * feedRateMultiplier;
            var target = bCommands[0];
            var targetDistance = MoveCommand.GetDistance(aLast, target);
            if (targetDistance > maxConnectionDistance)
            {
                if (VERBOSE)
                    Console.WriteLine($"- skipped connection at layer Z{a.LayerZ} position X{aLast.X.Value} Y{aLast.Y.Value} to Z{b.LayerZ} because of distance: {targetDistance}");
                return false;
            }
            else if (targetDistance < 0.0001)
            {
                if (VERBOSE)
                    Console.WriteLine($"- skipped connection at layer Z{a.LayerZ} position X{aLast.X.Value} Y{aLast.Y.Value} to Z{b.LayerZ} because of distance: {targetDistance}");
                return false;
            }
            var feed = relativeExtrusion ? feedRate * targetDistance : aLast.E.Value + feedRate * targetDistance;
            var newCommand = new MoveCommand(target.X, target.Y, target.Z, feed, aLast.F, " Connecting to next layer!");
            a.AllLines.Add(newCommand.ToString());
            if (VERBOSE)
                Console.WriteLine($"- connected at layer Z{a.LayerZ} position X{aLast.X.Value} Y{aLast.Y.Value} to Z{b.LayerZ} X{target.X.Value} Y{target.Y.Value} via command {newCommand}\t||| Distance: {targetDistance}\t||| Effective Feedrate: {feedRate}");
            return true;
        }
    }
}