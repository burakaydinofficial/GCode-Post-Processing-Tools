using System;
using System.Numerics;
using Common;
using Utilities;

namespace MyApp // Note: actual namespace depends on the project name.
{
    internal class Program
    {
        public const bool VERBOSE = true;

        public enum Mode
        {
            Unknown,
            Connect,
            Vase
        }

        static void Main(string[] args)
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

            var mode = argConnect ? Mode.Connect : argVase ? Mode.Vase : Mode.Unknown;
            var vaseTransitionLayers = 4;

            var filePathGiven = givenFilePath >= 0;
            var filePath = filePathGiven ? args[givenFilePath] : "C:\\Users\\burak\\Downloads\\Cable Winder Outer Shell_PLA_11m56s.gcode";
            var targetPath = filePathGiven ? filePath : filePath.Replace(".gcode", "_connected.gcode");
            var fileController = new FileController(filePath);
            var lines = fileController.ReadAllLines();
            var parsed = new ParsedFile(lines);
            ConsoleFileDumper logDumper = filePathGiven ? new ConsoleFileDumper("C:\\Temp\\ConnectSingleExtrusionLayers.log") : null;
            Console.WriteLine(parsed.GetLayerInfo());
            var reserialized = parsed.Parse();
            if (mode == Mode.Unknown)
            {
                Console.WriteLine(
                    "Select mode type \"connect\" to connect or type\"vase\" for vase mode and hit enter");
                var response = Console.ReadLine().ToLowerInvariant();
                if (response == "connect")
                    mode = Mode.Connect;
                if (response == "vase")
                    mode = Mode.Vase;
            }

            switch (mode)
            {
                case Mode.Unknown:
                    break;
                case Mode.Connect:
                    {
                        Console.WriteLine("Connecting");
                        int count = ConnectSingleExtrusionLayers(parsed.Layers);
                        Console.WriteLine($"Connected {count} layers!\nSaving");
                        var fileWriteController = new FileController(targetPath);
                        fileWriteController.WriteAllLines(parsed.Parse());
                        Console.WriteLine("Saved!");
                    }
                    break;
                case Mode.Vase:
                    {
                        Console.WriteLine("Vase ");
                        int count = VaseLayers(parsed.Layers, vaseTransitionLayers);
                        Console.WriteLine($"Vase {count} layers with {vaseTransitionLayers} transition layers!\nSaving");
                        var fileWriteController = new FileController(targetPath);
                        fileWriteController.WriteAllLines(parsed.Parse());
                        Console.WriteLine("Saved!");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
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
                feedRateMultiplier = a.LayerZ;
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
            var newCommand = new MoveCommand(-1, target.X, target.Y, target.Z, feed, aLast.F, " Connecting to next layer!");
            a.AllLines.Add(newCommand.ToString());
            if (VERBOSE)
                Console.WriteLine($"- connected at layer Z{a.LayerZ} position X{aLast.X.Value} Y{aLast.Y.Value} to Z{b.LayerZ} X{target.X.Value} Y{target.Y.Value} via command {newCommand}\t||| Distance: {targetDistance}\t||| Effective Feedrate: {feedRate}");
            return true;
        }

        public static int VaseLayers(IReadOnlyList<Layer> layers, int transitionLayerCount)
        {
            int totalLayerCount = 0;
            //var connectedLayers = ConnectSingleExtrusionLayers(layers);
            //if (connectedLayers < transitionLayerCount * 2)
            //    return 0;
            int groupStart = 0;
            int groupEnd = 0;
            while (groupStart < layers.Count && groupEnd < layers.Count)
            {
                if (!layers[groupStart].IsSingleExtrusion())
                {
                    groupStart++;
                    continue;
                }

                if (groupEnd < groupStart)
                    groupEnd = groupStart + 1;

                if (groupEnd + 1 < layers.Count && layers[groupEnd + 1].IsSingleExtrusion())
                {
                    groupEnd++;
                    continue;
                }

                if (groupEnd - groupStart > transitionLayerCount * 2)
                {
                    Console.WriteLine($"Found layers to vase between {groupStart}:Z{layers[groupStart].LayerZ} - {groupEnd}:Z{layers[groupEnd].LayerZ}");
                    var count = groupEnd - groupStart + 1;

                    VaseLayersInternal(layers.Skip(groupStart).Take(count), transitionLayerCount);

                    groupStart = groupEnd + 1;
                    totalLayerCount += count;
                }
            }
            return totalLayerCount;
        }

        public static void VaseLayersInternal(IEnumerable<Layer> layers, int transitionLayerCount)
        {
            var list = layers.ToList();
            var count = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                var edgeDistance = Math.Min(i, count - (i + 1));
                var layerWeight = Math.Clamp((float) edgeDistance / transitionLayerCount, 0f, 1f);
                VaseLayer(list[i], layerWeight);
            }
        }

        public static void VaseLayer(Layer layer, float weight)
        {
            var commands = layer.GetAllMoveCommands();
            var first = commands[0];
            var last = commands[^1];
            var totalLength = 0f;
            for (var i = 0; i < commands.Count - 1; i++)
            {
                totalLength += MoveCommand.GetDistance(commands[i], commands[i + 1]);
            }

            var startZ = layer.LayerZ;
            var zFix = layer.LayerHeight * weight;
            var endZ = layer.LayerZ + zFix;
            float totalMovement = 0f;
            for (var i = 0; i < commands.Count - 1; i++)
            {
                var from = commands[i];
                var to = commands[i + 1];
                totalMovement += MoveCommand.GetDistance(from, to);
                if (!to.Z.HasValue)
                {
                    var progress = totalMovement / totalLength;
                    to.Z = startZ + zFix * progress;
                    layer.AllLines[to.LineIndex] = to.ToString();
                }
            }
        }
    }
}