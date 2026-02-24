// FBX ASCII Rebase Rotation Tool
// by: vector_cmdr (https://github.com/vectorcmdr)
// 
// This tool processes ASCII FBX files in the current directory, identifies local rotations and scales, and moves them to their
// corresponding geometric properties.
// In doing so, it also cleans up the rotation values by fixing floating point errors, snapping values near +/-180 to exactly +/-180,
// and adjusting cases where X=0, Y=-180, and Z is negative by adding 360 to Z and negating it.
// Additionally, if it detects models with negative scaling, it can mirror the vertex positions along the negative axes and
// reverse the polygon winding order to correct the geometry.
//
// The tool outputs the number of modifications made and saves the fixed FBX files with "_fixed" appended to the original filename.
//
// It also processes Unity Prefab files and updates the Transform: m_LocalRotation: and m_LocalScale: properties
// to match the changes made in the FBX files, ensuring consistency between the model and prefab data.
//
// It does all of this with the intention of fixing common validator warnings related to submeshes that have been rotated or scaled
// in place and then merged into a parent mesh. For some reason, the Unity Asset Store team/devs and their dumb bot really hate that.
//
// Usage: Place this executable in the same directory as your .fbx and or .prefab files and run it.
// It will process all .fbx and .prefab files and output fixed versions.
//
// License: MIT License (https://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    // This is just here if I want to do some args stuff
    static bool doMirror = true;

    // Threshold below which a float is considered "zero" in normal data
    const double NearZeroThreshold = 1e-4;

    static void Main(string[] args)
    {
        string directory = AppContext.BaseDirectory;

        string[] fbxFiles = Directory.GetFiles(directory, "*.fbx");
        string[] prefabFiles = Directory.GetFiles(directory, "*.prefab");

        if (fbxFiles.Length == 0 && prefabFiles.Length == 0)
        {
            Console.WriteLine("No .fbx or .prefab files found in the current directory.");
            return;
        }

        foreach (string filePath in fbxFiles)
        {
            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_fixed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping already-fixed file: {Path.GetFileName(filePath)}");
                continue;
            }

            Console.WriteLine($"Processing FBX: {Path.GetFileName(filePath)}");

            if (!IsAsciiFbx(filePath))
            {
                Console.WriteLine($"  Skipped: '{Path.GetFileName(filePath)}' is binary FBX, not ASCII.");
                continue;
            }

            ProcessFbxFile(filePath);
        }

        foreach (string filePath in prefabFiles)
        {
            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_fixed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping already-fixed file: {Path.GetFileName(filePath)}");
                continue;
            }

            Console.WriteLine($"Processing Prefab: {Path.GetFileName(filePath)}");
            ProcessPrefabFile(filePath);
        }

        Console.WriteLine("\nDone. Press any key to exit.");
        Console.ReadKey();
    }

    /// <summary>
    ///  FBX Processing
    /// <summary>

    static void ProcessFbxFile(string filePath)
    {
        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(filePath));
            int rotationCount = 0;
            int scalingCount = 0;

            List<NegativeScaleModel> negativeScaleModels = new List<NegativeScaleModel>();

            bool inModelSection = false;
            bool inProperties70 = false;
            int braceDepthModel = 0;
            int braceDepthProperties = 0;
            int properties70StartIndex = -1;

            string currentModelId = "";
            string currentModelName = "";

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (!inModelSection && trimmed.StartsWith("Model:") && trimmed.Contains("\"Mesh\""))
                {
                    inModelSection = true;
                    braceDepthModel = 0;
                    braceDepthModel += CountChar(line, '{') - CountChar(line, '}');

                    Match modelMatch = Regex.Match(trimmed, @"^Model:\s*(\d+)\s*,\s*""Model::([^""]*)""\s*,");
                    if (modelMatch.Success)
                    {
                        currentModelId = modelMatch.Groups[1].Value;
                        currentModelName = modelMatch.Groups[2].Value;
                    }
                    else
                    {
                        currentModelId = "";
                        currentModelName = "";
                    }

                    continue;
                }

                if (inModelSection)
                {
                    braceDepthModel += CountChar(line, '{') - CountChar(line, '}');

                    if (!inProperties70 && trimmed.StartsWith("Properties70:"))
                    {
                        inProperties70 = true;
                        braceDepthProperties = 0;
                        braceDepthProperties += CountChar(line, '{') - CountChar(line, '}');
                        properties70StartIndex = i;
                        continue;
                    }

                    if (inProperties70)
                    {
                        braceDepthProperties += CountChar(line, '{') - CountChar(line, '}');

                        if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Rotation\""))
                        {
                            if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                    "Lcl Rotation", "GeometricRotation", "0,0,0",
                                    out double rotX, out double rotY, out double rotZ))
                            {
                                rotationCount++;

                                CleanupGeometricRotation(lines, i, properties70StartIndex, currentModelName);
                            }
                        }
                        else if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Scaling\""))
                        {
                            if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                    "Lcl Scaling", "GeometricScaling", "1,1,1",
                                    out double geoX, out double geoY, out double geoZ))
                            {
                                scalingCount++;

                                int negCount = 0;
                                if (geoX < 0) negCount++;
                                if (geoY < 0) negCount++;
                                if (geoZ < 0) negCount++;

                                if ((negCount == 1 || negCount == 3) && !string.IsNullOrEmpty(currentModelId))
                                {
                                    negativeScaleModels.Add(new NegativeScaleModel
                                    {
                                        ModelId = currentModelId,
                                        ModelName = currentModelName,
                                        ScaleX = geoX,
                                        ScaleY = geoY,
                                        ScaleZ = geoZ,
                                        NegativeCount = negCount,
                                        Properties70Start = properties70StartIndex
                                    });
                                }
                            }
                        }

                        if (braceDepthProperties <= 0)
                        {
                            inProperties70 = false;
                            properties70StartIndex = -1;
                        }
                    }

                    if (braceDepthModel <= 0)
                    {
                        inModelSection = false;
                        inProperties70 = false;
                        properties70StartIndex = -1;
                        currentModelId = "";
                        currentModelName = "";
                    }
                }
            }

            int mirrorCount = 0;
            foreach (var model in negativeScaleModels)
            {
                if (ProcessNegativeScaleModel(lines, model))
                {
                    mirrorCount++;
                }
            }

            int totalModifications = rotationCount + scalingCount;

            if (totalModifications > 0 || mirrorCount > 0)
            {
                string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_fixed.fbx";
                string outputPath = Path.Combine(Path.GetDirectoryName(filePath)!, outputFileName);
                File.WriteAllLines(outputPath, lines);
                Console.WriteLine($"  {rotationCount} rotation(s) and {scalingCount} scaling(s) moved to Geometric properties.");
                if (mirrorCount > 0)
                    Console.WriteLine($"  {mirrorCount} model(s) had geometry corrected due to negative scale.");
                Console.WriteLine($"  Saved: {outputFileName}");
            }
            else
            {
                Console.WriteLine("  No 'Lcl Rotation' or 'Lcl Scaling' entries found to modify.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing FBX file: {ex.Message}");
        }
    }

    struct NegativeScaleModel
    {
        public string ModelId;
        public string ModelName;
        public double ScaleX;
        public double ScaleY;
        public double ScaleZ;
        public int NegativeCount;
        public int Properties70Start;
    }

    static List<int> GetNegativeAxes(NegativeScaleModel model)
    {
        List<int> axes = new List<int>();
        if (model.ScaleX < 0) axes.Add(0);
        if (model.ScaleY < 0) axes.Add(1);
        if (model.ScaleZ < 0) axes.Add(2);
        return axes;
    }

    /// <summary>
    /// Finds the GeometricRotation line in a Properties70 block and cleans up its values:
    /// * Near-zero FPE -> 0
    /// * Near +/-180 → exactly ±180
    /// * X=0, Y=-180, -Z= (Z + 360) * -1
    /// </summary>
    static void CleanupGeometricRotation(List<string> lines, int lclRotLineIndex, int properties70StartIndex, string modelName)
    {
        int geoRotIndex = FindPropertyLine(lines, lclRotLineIndex, properties70StartIndex, "GeometricRotation");
        if (geoRotIndex < 0)
            return;

        string geoLine = lines[geoRotIndex];
        string geoTrimmed = geoLine.TrimStart();
        string geoIndent = geoLine.Substring(0, geoLine.Length - geoTrimmed.Length);

        Match match = Regex.Match(geoTrimmed,
            @"^P:\s*""GeometricRotation""\s*,\s*""Vector3D""\s*,\s*""Vector""\s*,\s*""[^""]*""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$");

        if (!match.Success)
            return;

        double x = 0, y = 0, z = 0;
        double.TryParse(match.Groups[1].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        double.TryParse(match.Groups[2].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        double.TryParse(match.Groups[3].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);

        bool changed = false;

        if (Math.Abs(x) < NearZeroThreshold && x != 0) { x = 0; changed = true; }
        if (Math.Abs(y) < NearZeroThreshold && y != 0) { y = 0; changed = true; }
        if (Math.Abs(z) < NearZeroThreshold && z != 0) { z = 0; changed = true; }

        if (Math.Abs(x - 180.0) <= 0.001 && x != 180.0) { x = 180; changed = true; }
        if (Math.Abs(x + 180.0) <= 0.001 && x != -180.0) { x = -180; changed = true; }
        if (Math.Abs(y - 180.0) <= 0.001 && y != 180.0) { y = 180; changed = true; }
        if (Math.Abs(y + 180.0) <= 0.001 && y != -180.0) { y = -180; changed = true; }
        if (Math.Abs(z - 180.0) <= 0.001 && z != 180.0) { z = 180; changed = true; }
        if (Math.Abs(z + 180.0) <= 0.001 && z != -180.0) { z = -180; changed = true; }

        if (x == 0 && y == -180 && z < 0)
        {
            double newZ = (z + 360.0) * -1;
            Console.WriteLine($"  Rotation Z adjusted for model '{modelName}': {z} -> {newZ} (X=0, Y=-180, Z negative)");
            z = newZ;
            changed = true;
        }

        if (changed)
        {
            string xs = FormatDouble(x);
            string ys = FormatDouble(y);
            string zs = FormatDouble(z);
            lines[geoRotIndex] = $"{geoIndent}P: \"GeometricRotation\", \"Vector3D\", \"Vector\", \"\",{xs},{ys},{zs}";
            Console.WriteLine($"  GeometricRotation cleaned for model '{modelName}': {xs}, {ys}, {zs}");
        }
    }

    /// <summary>
    /// Inverts the Y rotation axis on the GeometricRotation line (multiplies Y by -1).
    /// </summary>
    static void InvertGeometricRotationY(List<string> lines, int properties70Start, string modelName)
    {
        // Search for GeometricRotation within the Properties70 block
        int geoRotIndex = -1;
        for (int j = properties70Start + 1; j < lines.Count; j++)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("}"))
                break;
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains("\"GeometricRotation\""))
            {
                geoRotIndex = j;
                break;
            }
        }

        if (geoRotIndex < 0)
            return;

        string geoLine = lines[geoRotIndex];
        string geoTrimmed = geoLine.TrimStart();
        string geoIndent = geoLine.Substring(0, geoLine.Length - geoTrimmed.Length);

        Match match = Regex.Match(geoTrimmed,
            @"^P:\s*""GeometricRotation""\s*,\s*""Vector3D""\s*,\s*""Vector""\s*,\s*""([^""]*)""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$");

        if (!match.Success)
            return;

        string flagValue = match.Groups[1].Value;
        double x = 0, y = 0, z = 0;
        double.TryParse(match.Groups[2].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        double.TryParse(match.Groups[3].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        double.TryParse(match.Groups[4].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);

        y *= -1.0;

        string xs = FormatDouble(x);
        string ys = FormatDouble(y);
        string zs = FormatDouble(z);

        lines[geoRotIndex] = $"{geoIndent}P: \"GeometricRotation\", \"Vector3D\", \"Vector\", \"\",{xs},{ys},{zs}";
        Console.WriteLine($"  GeometricRotation Y-axis inverted for model '{modelName}': {xs}, {ys}, {zs}");
    }

    /// <summary>
    /// Inverts the negative scale values on the GeometricScaling line (multiplies each
    /// negative component by -1 to make it positive).
    /// </summary>
    static void InvertNegativeGeometricScaling(List<string> lines, int properties70Start, string modelName)
    {
        int geoScaleIndex = -1;
        for (int j = properties70Start + 1; j < lines.Count; j++)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("}"))
                break;
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains("\"GeometricScaling\""))
            {
                geoScaleIndex = j;
                break;
            }
        }

        if (geoScaleIndex < 0)
            return;

        string geoLine = lines[geoScaleIndex];
        string geoTrimmed = geoLine.TrimStart();
        string geoIndent = geoLine.Substring(0, geoLine.Length - geoTrimmed.Length);

        Match match = Regex.Match(geoTrimmed,
            @"^P:\s*""GeometricScaling""\s*,\s*""Vector3D""\s*,\s*""Vector""\s*,\s*""([^""]*)""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$");

        if (!match.Success)
            return;

        string flagValue = match.Groups[1].Value;
        double x = 0, y = 0, z = 0;
        double.TryParse(match.Groups[2].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        double.TryParse(match.Groups[3].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        double.TryParse(match.Groups[4].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);

        if (x < 0) x *= -1.0;
        if (y < 0) y *= -1.0;
        if (z < 0) z *= -1.0;

        string xs = FormatDouble(x);
        string ys = FormatDouble(y);
        string zs = FormatDouble(z);

        lines[geoScaleIndex] = $"{geoIndent}P: \"GeometricScaling\", \"Vector3D\", \"Vector\", \"\",{xs},{ys},{zs}";
        Console.WriteLine($"  GeometricScaling negative values inverted for model '{modelName}': {xs}, {ys}, {zs}");
    }

    /// <summary>
    /// Processes a model with negative scale axes:
    /// 1. If 3 negative axes: invert normals first
    /// 2. Mirror vertices along each negative axis
    /// 3. Reverse polygon winding order
    /// 4. Invert the negative GeometricScaling values to make them positive
    /// 5. Invert the Y rotation axis on GeometricRotation
    /// </summary>
    static bool ProcessNegativeScaleModel(List<string> lines, NegativeScaleModel model)
    {
        string modelId = model.ModelId;
        string modelName = model.ModelName;

        string geometryId = FindGeometryIdForModel(lines, modelId, modelName);
        if (geometryId == null)
            return false;

        int geometryLineIndex = FindGeometryObjectLine(lines, geometryId, modelName);
        if (geometryLineIndex < 0)
            return false;

        int geometryEndIndex = FindBlockEnd(lines, geometryLineIndex);

        bool anyWork = false;

        // If all 3 axes are negative, invert normals before mirroring
        if (model.NegativeCount == 3)
        {
            bool normalsInverted = InvertNormalsInGeometry(lines, geometryLineIndex, geometryEndIndex, modelName, geometryId);
            if (normalsInverted)
                anyWork = true;
        }

        // Mirror vertices and reverse winding
        if (doMirror)
        {
            List<int> negAxes = GetNegativeAxes(model);

            foreach (int axis in negAxes)
            {
                bool mirrored = MirrorVerticesInGeometry(lines, geometryLineIndex, geometryEndIndex, axis, modelName, geometryId);
                if (mirrored)
                {
                    Console.WriteLine($"  Vertices mirrored along {AxisName(axis)} axis for model: '{modelName}' (Geometry ID: {geometryId})");
                    anyWork = true;
                }
            }

            bool windingReversed = ReverseWindingOrder(lines, geometryLineIndex, geometryEndIndex, modelName, geometryId);
            if (windingReversed)
            {
                Console.WriteLine($"  Winding order reversed for model: '{modelName}' (Geometry ID: {geometryId})");
                anyWork = true;
            }
        }

        // Invert negative GeometricScaling values to make them positive
        InvertNegativeGeometricScaling(lines, model.Properties70Start, modelName);

        // If all 3 axes are negative, invert the Y rotation axis on GeometricRotation
        if (model.NegativeCount == 3)
            InvertGeometricRotationY(lines, model.Properties70Start, modelName);

        anyWork = true;

        return anyWork;
    }

    static string AxisName(int axis)
    {
        return axis switch { 0 => "X", 1 => "Y", 2 => "Z", _ => "?" };
    }

    static string FindGeometryIdForModel(List<string> lines, string modelId, string modelName)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("C:") && trimmed.Contains("\"OO\"") && trimmed.Contains(modelId))
            {
                Match connMatch = Regex.Match(trimmed, @"^C:\s*""OO""\s*,\s*(\d+)\s*,\s*(\d+)");
                if (connMatch.Success)
                {
                    string id1 = connMatch.Groups[1].Value;
                    string id2 = connMatch.Groups[2].Value;
                    string otherId = (id1 == modelId) ? id2 : id1;

                    if (i > 0)
                    {
                        string prevTrimmed = lines[i - 1].TrimStart();
                        if (prevTrimmed.Contains(";Geometry::"))
                        {
                            return otherId;
                        }
                    }
                }
            }
        }

        Console.WriteLine($"  Warning: Could not find Geometry connection for Model '{modelName}' (ID: {modelId}).");
        return null;
    }

    static int FindGeometryObjectLine(List<string> lines, string geometryId, string modelName)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("Geometry:") && trimmed.Contains(geometryId) && trimmed.Contains("\"Mesh\""))
            {
                Match geoMatch = Regex.Match(trimmed, @"^Geometry:\s*(\d+)");
                if (geoMatch.Success && geoMatch.Groups[1].Value == geometryId)
                {
                    return i;
                }
            }
        }

        Console.WriteLine($"  Warning: Could not find Geometry object for ID {geometryId} (Model '{modelName}').");
        return -1;
    }

    static int FindBlockEnd(List<string> lines, int startIndex)
    {
        int depth = 0;
        bool entered = false;
        for (int i = startIndex; i < lines.Count; i++)
        {
            depth += CountChar(lines[i], '{') - CountChar(lines[i], '}');
            if (depth > 0) entered = true;
            if (entered && depth <= 0)
                return i;
        }
        return lines.Count - 1;
    }

    /// <summary>
    /// Finds a P: property line by name within a Properties70 block, searching both
    /// backwards and forwards from a reference line.
    /// </summary>
    static int FindPropertyLine(List<string> lines, int refLineIndex, int properties70StartIndex, string propertyName)
    {
        // Search backwards
        for (int j = refLineIndex; j > properties70StartIndex; j--)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{propertyName}\""))
                return j;
        }

        // Search forwards
        for (int j = refLineIndex + 1; j < lines.Count; j++)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("}"))
                break;
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{propertyName}\""))
                return j;
        }

        return -1;
    }

    /// <summary>
    ///  Normal Inversion (only for 3-negative-axis models)
    /// </summary>

    static bool InvertNormalsInGeometry(List<string> lines, int geoStart, int geoEnd, string modelName, string geometryId)
    {
        for (int i = geoStart; i <= geoEnd; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("LayerElementNormal:"))
                continue;

            int layerEnd = FindBlockEnd(lines, i);

            for (int j = i; j <= layerEnd; j++)
            {
                string jtrimmed = lines[j].TrimStart();
                if (!jtrimmed.StartsWith("Normals:"))
                    continue;

                int normalsEnd = FindBlockEnd(lines, j);

                for (int k = j; k <= normalsEnd; k++)
                {
                    string ktrimmed = lines[k].TrimStart();
                    if (!ktrimmed.StartsWith("a:"))
                        continue;

                    ProcessFloatArray(lines, k, normalsEnd, (ref double val) =>
                    {
                        if (Math.Abs(val) < NearZeroThreshold)
                            val = 0;
                        else
                            val *= -1.0;
                    });

                    Console.WriteLine($"  Normals inverted for model: '{modelName}' (Geometry ID: {geometryId})");
                    return true;
                }
            }
        }

        Console.WriteLine($"  Warning: Could not find normals data for Model '{modelName}' (Geometry ID: {geometryId}).");
        return false;
    }

    /// <summary>
    ///  Vertex Mirroring
    /// </summary>

    static bool MirrorVerticesInGeometry(List<string> lines, int geoStart, int geoEnd, int axis, string modelName, string geometryId)
    {
        for (int i = geoStart; i <= geoEnd; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("Vertices:"))
                continue;

            int verticesEnd = FindBlockEnd(lines, i);

            for (int k = i; k <= verticesEnd; k++)
            {
                string ktrimmed = lines[k].TrimStart();
                if (!ktrimmed.StartsWith("a:"))
                    continue;

                ProcessFloatArrayIndexed(lines, k, verticesEnd, (int idx, ref double val) =>
                {
                    if (idx % 3 == axis)
                        val *= -1.0;
                });

                return true;
            }
        }

        Console.WriteLine($"  Warning: Could not find vertex data for Model '{modelName}' (Geometry ID: {geometryId}).");
        return false;
    }

    /// <summary>
    ///  Winding Order Reversal
    /// </summary>

    static bool ReverseWindingOrder(List<string> lines, int geoStart, int geoEnd, string modelName, string geometryId)
    {
        for (int i = geoStart; i <= geoEnd; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("PolygonVertexIndex:"))
                continue;

            int polyBlockEnd = FindBlockEnd(lines, i);

            for (int k = i; k <= polyBlockEnd; k++)
            {
                string ktrimmed = lines[k].TrimStart();
                if (!ktrimmed.StartsWith("a:"))
                    continue;

                List<int> indices = ReadIntArray(lines, k, polyBlockEnd);

                int polyStart = 0;
                for (int idx = 0; idx < indices.Count; idx++)
                {
                    if (indices[idx] < 0)
                    {
                        int polyEndIdx = idx;
                        int polyLen = polyEndIdx - polyStart + 1;

                        if (polyLen >= 3)
                        {
                            int lastRealIndex = -(indices[polyEndIdx] + 1);

                            int left = polyStart;
                            int right = polyEndIdx - 1;
                            while (left < right)
                            {
                                int tmp = indices[left];
                                indices[left] = indices[right];
                                indices[right] = tmp;
                                left++;
                                right--;
                            }

                            indices[polyEndIdx] = -(lastRealIndex + 1);
                        }

                        polyStart = idx + 1;
                    }
                }

                WriteIntArray(lines, k, polyBlockEnd, indices);

                return true;
            }
        }

        Console.WriteLine($"  Warning: Could not find polygon index data for Model '{modelName}' (Geometry ID: {geometryId}).");
        return false;
    }

    static List<int> ReadIntArray(List<string> lines, int aLineIndex, int blockEnd)
    {
        List<string> dataParts = new List<string>();

        string firstTrimmed = lines[aLineIndex].TrimStart();
        dataParts.Add(firstTrimmed.Substring(2).Trim());

        for (int k = aLineIndex + 1; k <= blockEnd; k++)
        {
            string contTrimmed = lines[k].TrimStart();
            if (contTrimmed.Length > 0 && !contTrimmed.StartsWith("}") &&
                !contTrimmed.Contains(":") &&
                (char.IsDigit(contTrimmed[0]) || contTrimmed[0] == '-' || contTrimmed[0] == ','))
            {
                dataParts.Add(contTrimmed);
            }
            else
            {
                break;
            }
        }

        string allData = string.Join("", dataParts);
        string[] valueStrings = allData.Split(',');
        List<int> result = new List<int>(valueStrings.Length);

        foreach (string vs in valueStrings)
        {
            string s = vs.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
                result.Add(val);
        }

        return result;
    }

    static void WriteIntArray(List<string> lines, int aLineIndex, int blockEnd, List<int> values)
    {
        string firstLine = lines[aLineIndex];
        string indent = firstLine.Substring(0, firstLine.Length - firstLine.TrimStart().Length);

        List<int> dataLineIndices = new List<int>();
        dataLineIndices.Add(aLineIndex);

        for (int k = aLineIndex + 1; k <= blockEnd; k++)
        {
            string contTrimmed = lines[k].TrimStart();
            if (contTrimmed.Length > 0 && !contTrimmed.StartsWith("}") &&
                !contTrimmed.Contains(":") &&
                (char.IsDigit(contTrimmed[0]) || contTrimmed[0] == '-' || contTrimmed[0] == ','))
            {
                dataLineIndices.Add(k);
            }
            else
            {
                break;
            }
        }

        string[] outputStrings = values.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();

        if (dataLineIndices.Count == 1)
        {
            lines[aLineIndex] = $"{indent}a: {string.Join(",", outputStrings)}";
        }
        else
        {
            int totalValues = outputStrings.Length;
            int lineCount = dataLineIndices.Count;
            int valsPerLine = Math.Max(1, (int)Math.Ceiling((double)totalValues / lineCount));

            int valIndex = 0;
            for (int li = 0; li < dataLineIndices.Count; li++)
            {
                int count = Math.Min(valsPerLine, totalValues - valIndex);
                if (count <= 0)
                {
                    lines[dataLineIndices[li]] = "";
                    continue;
                }

                string[] chunk = new string[count];
                Array.Copy(outputStrings, valIndex, chunk, 0, count);
                string joined = string.Join(",", chunk);
                bool hasMore = (valIndex + count) < totalValues;

                if (li == 0)
                {
                    lines[dataLineIndices[li]] = $"{indent}a: {joined}{(hasMore ? "," : "")}";
                }
                else
                {
                    lines[dataLineIndices[li]] = $"{indent}{joined}{(hasMore ? "," : "")}";
                }

                valIndex += count;
            }
        }
    }

    /// <summary>
    ///  Float Array Processing
    /// </summary>

    delegate void FloatModifier(ref double value);
    delegate void IndexedFloatModifier(int index, ref double value);

    static void ProcessFloatArray(List<string> lines, int aLineIndex, int blockEnd, FloatModifier modifier)
    {
        ProcessFloatArrayIndexed(lines, aLineIndex, blockEnd, (int idx, ref double val) => modifier(ref val));
    }

    static void ProcessFloatArrayIndexed(List<string> lines, int aLineIndex, int blockEnd, IndexedFloatModifier modifier)
    {
        string firstLine = lines[aLineIndex];
        string indent = firstLine.Substring(0, firstLine.Length - firstLine.TrimStart().Length);

        string firstTrimmed = firstLine.TrimStart();
        string dataStart = firstTrimmed.Substring(2).Trim();

        List<int> dataLineIndices = new List<int>();
        List<string> rawDataPerLine = new List<string>();

        dataLineIndices.Add(aLineIndex);
        rawDataPerLine.Add(dataStart);

        for (int k = aLineIndex + 1; k <= blockEnd; k++)
        {
            string contTrimmed = lines[k].TrimStart();
            if (contTrimmed.Length > 0 && !contTrimmed.StartsWith("}") &&
                !contTrimmed.Contains(":") &&
                (char.IsDigit(contTrimmed[0]) || contTrimmed[0] == '-' || contTrimmed[0] == ','))
            {
                dataLineIndices.Add(k);
                rawDataPerLine.Add(contTrimmed);
            }
            else
            {
                break;
            }
        }

        string allData = string.Join("", rawDataPerLine);
        string[] valueStrings = allData.Split(',');
        double[] values = new double[valueStrings.Length];

        for (int v = 0; v < valueStrings.Length; v++)
        {
            string valStr = valueStrings[v].Trim();
            if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                values[v] = val;
        }

        for (int v = 0; v < values.Length; v++)
        {
            modifier(v, ref values[v]);
        }

        string[] outputStrings = new string[values.Length];
        for (int v = 0; v < values.Length; v++)
        {
            outputStrings[v] = FormatDouble(values[v]);
        }

        if (dataLineIndices.Count == 1)
        {
            lines[dataLineIndices[0]] = $"{indent}a: {string.Join(",", outputStrings)}";
        }
        else
        {
            int totalValues = outputStrings.Length;
            int lineCount = dataLineIndices.Count;
            int valsPerLine = Math.Max(1, (int)Math.Ceiling((double)totalValues / lineCount));

            int valIndex = 0;
            for (int li = 0; li < dataLineIndices.Count; li++)
            {
                int count = Math.Min(valsPerLine, totalValues - valIndex);
                if (count <= 0)
                {
                    lines[dataLineIndices[li]] = "";
                    continue;
                }

                string[] chunk = new string[count];
                Array.Copy(outputStrings, valIndex, chunk, 0, count);
                string joined = string.Join(",", chunk);
                bool hasMore = (valIndex + count) < totalValues;

                if (li == 0)
                {
                    lines[dataLineIndices[li]] = $"{indent}a: {joined}{(hasMore ? "," : "")}";
                }
                else
                {
                    lines[dataLineIndices[li]] = $"{indent}{joined}{(hasMore ? "," : "")}";
                }

                valIndex += count;
            }
        }
    }

    static string FormatDouble(double value)
    {
        if (value == 0.0)
            return "0";

        return value.ToString("G", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///  Lcl Property Processing
    /// </summary>

    static bool ProcessLclProperty(List<string> lines, ref int currentIndex, int properties70StartIndex,
        string lclName, string geometricName, string defaultValues)
    {
        return ProcessLclProperty(lines, ref currentIndex, properties70StartIndex,
            lclName, geometricName, defaultValues, out _, out _, out _);
    }

    static bool ProcessLclProperty(List<string> lines, ref int currentIndex, int properties70StartIndex,
        string lclName, string geometricName, string defaultValues,
        out double geoX, out double geoY, out double geoZ)
    {
        geoX = 0; geoY = 0; geoZ = 0;

        string line = lines[currentIndex];
        string trimmed = line.TrimStart();

        string escapedName = Regex.Escape(lclName);
        string pattern = $@"^P:\s*""{escapedName}""\s*,\s*""{escapedName}""\s*,\s*""[^""]*""\s*,\s*""([^""]*)""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$";

        Match match = Regex.Match(trimmed, pattern);
        if (!match.Success)
            return false;

        string aValue = match.Groups[1].Value;
        string x = match.Groups[2].Value.Trim();
        string y = match.Groups[3].Value.Trim();
        string z = match.Groups[4].Value.Trim();

        double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out geoX);
        double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out geoY);
        double.TryParse(z, NumberStyles.Float, CultureInfo.InvariantCulture, out geoZ);

        string indent = line.Substring(0, line.Length - trimmed.Length);

        int existingGeoIndex = -1;
        for (int j = currentIndex - 1; j > properties70StartIndex; j--)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{geometricName}\""))
            {
                existingGeoIndex = j;
                break;
            }
        }

        if (existingGeoIndex < 0)
        {
            for (int j = currentIndex + 1; j < lines.Count; j++)
            {
                string checkTrimmed = lines[j].TrimStart();
                if (checkTrimmed.StartsWith("}"))
                    break;
                if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{geometricName}\""))
                {
                    existingGeoIndex = j;
                    break;
                }
            }
        }

        if (existingGeoIndex >= 0)
        {
            string geoIndent = lines[existingGeoIndex].Substring(0,
                lines[existingGeoIndex].Length - lines[existingGeoIndex].TrimStart().Length);
            lines[existingGeoIndex] = $"{geoIndent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
        }
        else
        {
            string geometricLine = $"{indent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
            lines.Insert(currentIndex, geometricLine);
            currentIndex++;
        }

        lines[currentIndex] = $"{indent}P: \"{lclName}\", \"{lclName}\", \"\", \"{aValue}\",{defaultValues}";

        return true;
    }

    /// <summary>
    ///  Prefab (Unity YAML) Processing
    /// </summary>

    static void ProcessPrefabFile(string filePath)
    {
        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(filePath));
            int rotationCount = 0;
            int scalingCount = 0;

            bool inTransform = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int currentIndent = GetYamlIndent(line);
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("---"))
                {
                    inTransform = false;
                    continue;
                }

                if (trimmed.StartsWith("--- !u!4 ") || trimmed.StartsWith("--- !u!4&"))
                {
                    inTransform = true;
                    continue;
                }

                if (trimmed == "Transform:" || trimmed.StartsWith("Transform:"))
                {
                    inTransform = true;
                    continue;
                }

                if (inTransform)
                {
                    if (trimmed.StartsWith("m_LocalRotation:"))
                    {
                        if (trimmed.Contains("{"))
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}";
                            rotationCount++;
                        }
                        else
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}";

                            while (i + 1 < lines.Count)
                            {
                                string nextTrimmed = lines[i + 1].TrimStart();
                                int nextIndent = GetYamlIndent(lines[i + 1]);
                                if (nextIndent > currentIndent &&
                                    (nextTrimmed.StartsWith("x:") || nextTrimmed.StartsWith("y:") ||
                                     nextTrimmed.StartsWith("z:") || nextTrimmed.StartsWith("w:")))
                                {
                                    lines.RemoveAt(i + 1);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            rotationCount++;
                        }
                        continue;
                    }

                    if (trimmed.StartsWith("m_LocalScale:"))
                    {
                        if (trimmed.Contains("{"))
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalScale: {{x: 1, y: 1, z: 1}}";
                            scalingCount++;
                        }
                        else
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalScale: {{x: 1, y: 1, z: 1}}";

                            while (i + 1 < lines.Count)
                            {
                                string nextTrimmed = lines[i + 1].TrimStart();
                                int nextIndent = GetYamlIndent(lines[i + 1]);
                                if (nextIndent > currentIndent &&
                                    (nextTrimmed.StartsWith("x:") || nextTrimmed.StartsWith("y:") ||
                                     nextTrimmed.StartsWith("z:")))
                                {
                                    lines.RemoveAt(i + 1);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            scalingCount++;
                        }
                        continue;
                    }
                }
            }

            int totalModifications = rotationCount + scalingCount;

            if (totalModifications > 0)
            {
                string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_fixed.prefab";
                string outputPath = Path.Combine(Path.GetDirectoryName(filePath)!, outputFileName);
                File.WriteAllLines(outputPath, lines);
                Console.WriteLine($"  {rotationCount} rotation(s) and {scalingCount} scaling(s) reset in Transform components. Saved: {outputFileName}");
            }
            else
            {
                Console.WriteLine("  No Transform m_LocalRotation or m_LocalScale entries found to modify.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing Prefab file: {ex.Message}");
        }
    }

    static int GetYamlIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    /// <summary>
    ///  Shared Utilities
    /// </summary>

    static bool IsAsciiFbx(string filePath)
    {
        try
        {
            byte[] header = new byte[23];
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(header, 0, header.Length);
                if (bytesRead < header.Length)
                    return false;
            }

            string binaryMagic = "Kaydara FBX Binary";
            string headerStr = System.Text.Encoding.ASCII.GetString(header, 0, binaryMagic.Length);

            return !headerStr.Equals(binaryMagic, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    static int CountChar(string s, char c)
    {
        int count = 0;
        foreach (char ch in s)
        {
            if (ch == c) count++;
        }
        return count;
    }
}