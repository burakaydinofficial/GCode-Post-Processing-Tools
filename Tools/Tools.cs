using Common;
using System.Numerics;

namespace Tools
{
    public class Tools
    {
        private readonly bool VERBOSE;

        public Tools(bool verbose)
        {
            VERBOSE = verbose;
        }


        public int ConnectSingleExtrusionLayers(IReadOnlyList<Layer> layers)
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

        public bool ConnectSingleExtrusionLayers(Layer a, Layer b, bool relativeExtrusion, float maxConnectionDistance = 2f, float feedRateMultiplier = 0.8f)
        {
            if (!a.IsSingleExtrusion() || !b.IsSingleExtrusion())
            {
                return false;
            }

            if (a.LayerZ < 8)
            {
                feedRateMultiplier = a.LayerZ;
            }

            var aCommands = a.GetAllMoveCommands(true);
            var bCommands = b.GetAllMoveCommands(true);
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

        public int VaseLayers(IReadOnlyList<Layer> layers, int transitionLayerCount, bool connect = false, bool align = false)
        {
            int totalLayerCount = 0;
            if (connect)
            {
                var connectedLayers = ConnectSingleExtrusionLayers(layers);
                if (connectedLayers < transitionLayerCount * 2)
                    return 0;
            }
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

                    VaseLayersInternal(layers.Skip(groupStart).Take(count), transitionLayerCount, align);

                    groupStart = groupEnd + 1;
                    totalLayerCount += count;
                }
            }
            return totalLayerCount;
        }

        public void VaseLayersInternal(IEnumerable<Layer> layers, int transitionLayerCount, bool align)
        {
            var list = layers.ToList();
            var count = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                var edgeDistance = Math.Min(i, count - (i + 1));
                var layerWeight = Math.Clamp((float)edgeDistance / transitionLayerCount, 0f, 1f);
                VaseLayer(list[i], layerWeight);
                if (align && i < list.Count - 1)
                {
                    AlignLayer(list[i], list[i + 1], 1f, 0.9f, 5f);
                }
            }
        }

        public void VaseLayer(Layer layer, float weight)
        {
            var commands = layer.GetAllMoveCommands(true);
            var first = commands[0];
            var last = commands[^1];
            var totalLength = MoveCommand.GetLength(commands);

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
                    layer.Update(to);
                }
            }
        }

        public bool AlignLayer(Layer layer, Layer reference, float intensity, float initialAlignmentThreshold = 0.9f, float maxLength = 1f, float lengthRatio = 0.2f)
        {
            var commands = layer.GetAllMoveCommands(true);

            if (commands.Count < 2) return false;

            var totalLength = MoveCommand.GetLength(commands);
            float length = Math.Min(totalLength * lengthRatio, maxLength);

            var lastPoint = commands[^1];
            var last2Point = commands[^2];
            var finalVector = MoveCommand.CreateVector2(last2Point, lastPoint);

            var referenceCommands = reference.GetAllMoveCommands(true);

            if (referenceCommands.Count < 2) return false;

            var firstPoint = referenceCommands[0];
            var secondPoint = referenceCommands[1];
            var startingVector = MoveCommand.CreateVector2(firstPoint, secondPoint);
            var startingVectorNormalized = startingVector / startingVector.Length();
            var dot = Vector2.Dot(finalVector, startingVector) / (finalVector.Length() * startingVector.Length());
            if (dot < initialAlignmentThreshold)
            {
                return false;
            }

            var space = MoveCommand.CreateVector2(lastPoint, firstPoint);
            var offset = space - (space.Length() / finalVector.Length() * dot * finalVector);

            float remainingLength = length;
            int index = commands.Count - 1;
            while (remainingLength > 0)
            {
                float weight = intensity * remainingLength / length;
                var from = commands[index - 1];
                var to = commands[index];


                var usedLength = AlignExtrusion(from, to, startingVectorNormalized, offset, weight, remainingLength);
                if (usedLength > 0)
                {
                    layer.Update(from);
                    remainingLength -= usedLength;
                }

                if (index == commands.Count - 1)
                {
                    // TODO: add offset to "to" command
                }

                index--;
            }

            return true;
        }

        private float AlignExtrusion(MoveCommand extrusionFrom, MoveCommand extrusionTo, Vector2 targetVector, Vector2 offset, float weight, float remainingLength)
        {
            if (!extrusionFrom.Can2D() || !extrusionTo.Can2D())
            {
                return 0f;
            }
            var length = MoveCommand.GetDistance(extrusionTo, extrusionFrom);
            if (length > remainingLength)
            {
                weight *= remainingLength / length;
                length = remainingLength;
            }

            var currentVector = MoveCommand.CreateVector2(extrusionFrom, extrusionTo);
            var currentVectorNormalized = currentVector / length;
            var newVector = Vector2.Lerp(currentVectorNormalized, targetVector, weight) * length;
            extrusionFrom.X = extrusionTo.X - newVector.X + offset.X * weight;
            extrusionFrom.Y = extrusionTo.Y - newVector.Y + offset.Y * weight;

            return length;
        }
    }
}