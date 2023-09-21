using System.Globalization;
using System.Numerics;
using System.Text;
using Utilities;

namespace Utilities
{
    public class FileController
    {
        public readonly string Path;

        public FileController(string path)
        {
            Path = path;
        }

        public string[] ReadAllLines()
        {
            return File.ReadAllLines(Path);
        }

        public bool WriteAllLines(string[] lines)
        {
            try
            {
                File.WriteAllLines(Path, lines);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
        }
    }

    public class ConsoleFileDumper : IDisposable, IAsyncDisposable
    {
        private readonly FileStream ostrm;
        private readonly StreamWriter writer;
        private readonly TextWriter oldOut;

        public ConsoleFileDumper(string filePath)
        {
            oldOut = Console.Out;
            try
            {
                ostrm = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
                writer = new StreamWriter(ostrm);
            }
            catch (Exception e)
            {
                Console.WriteLine("Cannot open Redirect.txt for writing");
                Console.WriteLine(e.Message);
                return;
            }
            Console.SetOut(writer);
        }

        public void Dispose()
        {
            Console.SetOut(oldOut);
            writer.Flush();
            ostrm.Dispose();
            oldOut.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Console.SetOut(oldOut);
            await ostrm.FlushAsync();
            await ostrm.DisposeAsync();
            await oldOut.DisposeAsync();
        }
    }

    public class ParserHelpers
    {

        public static bool TryParse(string s, out string keyword, out float value)
        {
            keyword = null;
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;
            var subs = s.Substring(1);
            if (subs.StartsWith('.')) subs = '0' + subs;
            if (subs.StartsWith("-.")) subs = subs.Replace("-.", "-0.");
            if (subs.Length > 0)
            {
                bool success = TryParse(subs, out value);
                if (!success)
                    return false;
                keyword = s.Substring(0, 1);
                return true;
            }

            return false;
        }

        public static bool TryParse(string subs, out float value)
        {
            return float.TryParse(subs, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        public static void Append(StringBuilder strB, string key, float value)
        {
            strB.AppendFormat(CultureInfo.InvariantCulture, " {0}{1:0.#####}", key, value);
            //var value = value.ToString(CultureInfo.InvariantCulture);
            //strB.Append(value);
        }
    }
}

namespace Common
{

    public class CommandSelector
    {

    }

    public class ParsedFile
    {
        public readonly LayerBase Intro;
        public readonly List<Layer> Layers;
        public readonly LayerBase Outro;

        public ParsedFile(string[] lines)
        {
            var list = lines.ToList();
            var firstLayer = list.FindIndex(x => x.StartsWith(";LAYER_CHANGE"));
            Intro = new LayerBase(list.Take(firstLayer));
            var outroStart = list.FindIndex(x => x.StartsWith("; EXECUTABLE_BLOCK_END"));
            var clearList = list.Skip(firstLayer);
            if (outroStart > -1)
            {
                Outro = new LayerBase(list.Skip(outroStart));
                clearList = clearList.Take(outroStart - firstLayer);
            }

            Layers = new List<Layer>();
            var currentList = clearList;
            using var enumerator = currentList.GetEnumerator();
            List<string> lineList = new List<string>();
            while (enumerator.MoveNext())
            {
                do
                {
                    lineList.Add(enumerator.Current);
                } while (enumerator.MoveNext() && !enumerator.Current.StartsWith(";LAYER_CHANGE"));
                Layers.Add(new Layer(lineList));
                lineList.Clear();
                lineList.Add(enumerator.Current);
            }
        }

        public string[] Parse()
        {
            List<string> lines = new List<string>();
            lines.AddRange(Intro.AllLines);
            foreach (var layer in Layers)
            {
                lines.AddRange(layer.AllLines);
            }
            lines.AddRange(Outro.AllLines);
            return lines.ToArray();
        }

        public string GetLayerInfo()
        {
            return string.Join('\n', Layers.ConvertAll(x => x.GetInfo()));
        }
    }

    public class LayerBase
    {
        public readonly List<string> AllLines;

        public LayerBase(IEnumerable<string> lines)
        {
            AllLines = new List<string>(lines);
        }
    }

    public class Layer : LayerBase
    {
        public float LayerZ;
        public float LayerHeight;

        public List<MoveCommand> GetAllMoveCommands(bool onlyCan2D = false)
        {
            List<MoveCommand> newList = new List<MoveCommand>();
            for (var i = 0; i < AllLines.Count; i++)
            {
                var line = AllLines[i];
                if (MoveCommand.IsMoveCommand(line))
                {
                    var command = new MoveCommand(line, i);
                    if (!onlyCan2D || command.Can2D())
                        newList.Add(command);
                }
            }
            return newList;
        }

        /// <summary>
        /// Expensive method do not call frequently
        /// </summary>
        public float GetLength()
        {
            return MoveCommand.GetLength(GetAllMoveCommands(true));
        }

        public Layer(IEnumerable<string> lines) : base(lines)
        {
            var zDeclaration = AllLines.Find(x => x.StartsWith(";Z:"));
            if (zDeclaration != null)
            {
                ParserHelpers.TryParse(zDeclaration.Substring(3), out LayerZ);
                //LayerZ = float.Parse(zDeclaration.AsSpan(3));
            }
            var heightDeclaration = AllLines.Find(x => x.StartsWith(";HEIGHT:"));
            if (heightDeclaration != null)
            {
                ParserHelpers.TryParse(heightDeclaration.Substring(8), out LayerHeight);
                //LayerHeight = float.Parse(heightDeclaration.AsSpan(8));
            }
        }

        public bool IsSingleExtrusion()
        {
            var allMoveCommands = GetAllMoveCommands(true);
            var firstExtrusion = allMoveCommands.FindIndex(x => x.E.HasValue);
            if (firstExtrusion != -1)
            {
                var nextNonExtrusion = allMoveCommands.FindIndex(firstExtrusion, x => !x.E.HasValue);
                return nextNonExtrusion == -1;
            }
            return false;
        }

        public virtual string GetInfo()
        {
            return
                $"Layer Z: {LayerZ} Height: {LayerHeight} Line Count: {AllLines.Count} IsSingleExtrusion: {IsSingleExtrusion()}";
        }

        public void Update(MoveCommand command)
        {
            if (command.LineIndex >= 0)
            {
                AllLines[command.LineIndex] = command.ToString();
            }
        }
    }

    public class MoveCommand
    {
        public readonly int LineIndex;
        public float? X;
        public float? Y;
        public float? Z;
        public float? E;
        public float? F;
        public string Comment;

        public MoveCommand(int lineIndex, float? x, float? y, float? z, float? e, float? f, string comment)
        {
            LineIndex = lineIndex;
            X = x;
            Y = y;
            Z = z;
            E = e;
            F = f;
            Comment = comment;
        }

        public MoveCommand(string line, int lineIndex)
        {
            LineIndex = lineIndex;
            if (string.IsNullOrWhiteSpace(line))
                throw new ArgumentException();
            var splitted = line.Split(' ');
            if (splitted[0] != "G1")
                throw new ArgumentException();

            bool comment = false;

            foreach (var s in splitted)
            {
                if (ParserHelpers.TryParse(s, out string keyword, out float parsed))
                {
                    switch (keyword)
                    {
                        case "X": X = parsed; break;
                        case "Y": Y = parsed; break;
                        case "Z": Z = parsed; break;
                        case "E": E = parsed; break;
                        case "F": F = parsed; break;
                    }
                }
                if (s.StartsWith(';'))
                {
                    comment = true;
                    break;
                }
            }

            if (comment)
            {
                var index = line.IndexOf(';');
                Comment = line.Substring(index + 1);
            }
        }

        public override string ToString()
        {
            var strB = new StringBuilder("G1");
            if (X.HasValue)
            {
                ParserHelpers.Append(strB, "X", X.Value);
            }
            if (Y.HasValue)
            {
                ParserHelpers.Append(strB, "Y", Y.Value);
            }
            if (Z.HasValue)
            {
                ParserHelpers.Append(strB, "Z", Z.Value);
            }
            if (E.HasValue)
            {
                ParserHelpers.Append(strB, "E", E.Value);
            }
            if (F.HasValue)
            {
                ParserHelpers.Append(strB, "F", F.Value);
            }

            if (!string.IsNullOrWhiteSpace(Comment))
            {
                strB.Append(" ;");
                strB.Append(Comment);
            }
            return strB.ToString();
        }

        public bool Can2D() => X.HasValue && Y.HasValue;

        public bool Can3D() => X.HasValue && Y.HasValue && Z.HasValue;

        public static bool IsMoveCommand(string line)
        {
            return !string.IsNullOrWhiteSpace(line) && line.StartsWith("G1");
        }

        public static Vector2 CreateVector2(MoveCommand from, MoveCommand to)
        {
            return new Vector2(
                to.X.HasValue && from.X.HasValue ? (float)to.X.Value - (float)from.X.Value : 0,
                to.Y.HasValue && from.Y.HasValue ? (float)to.Y.Value - (float)from.Y.Value : 0
                );
        }
        public static Vector3 CreateVector3(MoveCommand from, MoveCommand to)
        {
            return new Vector3(
                to.X.HasValue && from.X.HasValue ? (float)to.X.Value - (float)from.X.Value : 0,
                to.Y.HasValue && from.Y.HasValue ? (float)to.Y.Value - (float)from.Y.Value : 0,
                to.Z.HasValue && from.Z.HasValue ? (float)to.Z.Value - (float)from.Z.Value : 0
                );
        }

        public static float GetDistance(MoveCommand a, MoveCommand b)
        {
            return CreateVector2(a, b).Length();
        }

        public static float GetLength(IReadOnlyList<MoveCommand> commands)
        {

            var totalLength = 0f;
            for (var i = 0; i < commands.Count - 1; i++)
            {
                totalLength += MoveCommand.GetDistance(commands[i], commands[i + 1]);
            }
            return totalLength;
        }
    }
}