using System;
using System.Collections.Generic;
using System.Linq;
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

            //if (a.LayerZ < 8)
            //{
            //    feedRateMultiplier = a.LayerZ;
            //}

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

        public int VaseLayers(IReadOnlyList<Layer> layers, int transitionLayerCount, bool connect = false, AlignConfig align = null)
        {
            int totalLayerCount = 0;
            if (connect && align == null)
            {
                ConnectSingleExtrusionLayers(layers);
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

            if (connect && align != null)
            {
                ConnectSingleExtrusionLayers(layers);
            }
            return totalLayerCount;
        }

        public void VaseLayersInternal(IEnumerable<Layer> layers, int transitionLayerCount, AlignConfig align)
        {
            var list = layers.ToList();
            var count = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                var edgeDistance = Math.Min(i, count - (i + 1));
                var layerWeight = Math.Clamp((float)edgeDistance / transitionLayerCount, 0f, 1f);
                VaseLayer(list[i], layerWeight);
                if (align != null && i < list.Count - 1)
                {
                    AlignLayer(list[i], list[i + 1], align);
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

        private static Vector2 TakeStartingVector(List<MoveCommand> commands, float length)
        {
            float distanceMoved = 0f;
            Vector2 vec = Vector2.Zero;
            int index = 0;
            while (distanceMoved < length)
            {
                var newVec = MoveCommand.CreateVector2(commands[index], commands[index + 1]);
                distanceMoved += newVec.Length();
                vec = vec + newVec;
                index++;
            }

            return vec;
        }

        private static Vector2 TakeFinalVector(List<MoveCommand> commands, float length)
        {
            float distanceMoved = 0f;
            Vector2 vec = Vector2.Zero;
            int index = 0;
            while (distanceMoved < length)
            {
                var newVec = MoveCommand.CreateVector2(commands[index^2], commands[index^1]);
                distanceMoved += newVec.Length();
                vec = vec + newVec;
                index++;
            }

            return vec;
        }

        public bool AlignLayer(Layer layer, Layer reference, AlignConfig config)
        {
            var commands = layer.GetAllMoveCommands(true);

            if (commands.Count < 2) return false;

            var totalLength = MoveCommand.GetLength(commands);
            float length = Math.Min(totalLength * config.LengthRatio, config.MaxLength);
            float finalVectorLength = Math.Min(totalLength * config.FinalVectorSampleLengthRatio,
                config.FinalVectorMaxSampleLength);

            var lastPoint = commands[^1];
            var finalVector = TakeFinalVector(commands, finalVectorLength);

            var referenceCommands = reference.GetAllMoveCommands(true);

            if (referenceCommands.Count < 2) return false;

            var referenceLength = Math.Min(MoveCommand.GetLength(referenceCommands) * config.ReferenceVectorSampleLengthRatio,
                config.ReferenceVectorMaxSampleLength);

            var firstPoint = referenceCommands[0];

            var referenceVector = TakeStartingVector(referenceCommands, referenceLength);
            var referenceVectorNormalized = referenceVector / referenceVector.Length();

            var dot = Vector2.Dot(finalVector, referenceVectorNormalized) / (finalVector.Length());
            if (dot < config.InitialAlignmentThreshold)
            {
                return false;
            }

            var space = MoveCommand.CreateVector2(lastPoint, firstPoint);
            var spaceLength = space.Length();
            var offset = space - (spaceLength / finalVector.Length() * dot * finalVector);

            float remainingLength = length;
            int index = commands.Count - 1;
            while (remainingLength > 0 && index > 1)
            {
                var from = commands[index - 1];
                var to = commands[index];

                float weight = config.Intensity * (remainingLength - spaceLength - MoveCommand.GetDistance(from, to)) / length;
                weight = Math.Max(0, weight);


                var usedLength = AlignExtrusion(from, to, referenceVectorNormalized, offset, weight, remainingLength, config);
                if (usedLength > 0)
                {
                    layer.Update(from);
                    remainingLength -= usedLength;
                }

                if (index == commands.Count - 1)
                {
                    // TODO: add offset to "to" command
                    to.X = to.X.Value + offset.X;
                    to.Y = to.Y.Value + offset.Y;
                    layer.Update(to);
                }

                index--;
            }

            return true;
        }

        private float AlignExtrusion(MoveCommand extrusionFrom, MoveCommand extrusionTo, Vector2 targetVector, Vector2 offset, float weight, float remainingLength, AlignConfig config)
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

            var offsetWeight = (float)Math.Pow(weight, config.OffsetWeightPower);

            var currentVector = MoveCommand.CreateVector2(extrusionFrom, extrusionTo);
            var currentVectorNormalized = currentVector / length;
            var newVector = Vector2.Lerp(currentVectorNormalized, targetVector, offsetWeight) * length;
            extrusionFrom.X = extrusionTo.X - newVector.X + offset.X * offsetWeight;
            extrusionFrom.Y = extrusionTo.Y - newVector.Y + offset.Y * offsetWeight;

            return length;
        }
    }



    [Serializable]
    public class AlignConfig
    {
        public float Intensity = 1f;
        public float InitialAlignmentThreshold = 0.9f;
        public float MaxLength = 5f;
        public float LengthRatio = 0.2f;
        public float OffsetWeightPower = 1.2f;
        public float ReferenceVectorMaxSampleLength = 1f;
        public float ReferenceVectorSampleLengthRatio = 0.2f;
        public float FinalVectorMaxSampleLength = 1f;
        public float FinalVectorSampleLengthRatio = 0.2f;

        public AlignConfig()
        {

        }

        public AlignConfig(float intensity = 1f, float initialAlignmentThreshold = 0.9f, float maxLength = 5f, float lengthRatio = 0.2f, float offsetWeightPower = 1.2f)
        {
            Intensity = intensity;
            InitialAlignmentThreshold = initialAlignmentThreshold;
            MaxLength = maxLength;
            LengthRatio = lengthRatio;
            OffsetWeightPower = offsetWeightPower;
        }
    }

}