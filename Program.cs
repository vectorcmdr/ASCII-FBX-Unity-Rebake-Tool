// FBX ASCII Rebase Rotation Tool
// by: vector_cmdr (https://github.com/vectorcmdr)
// 
// This tool processes ASCII FBX files in the current directory, identifies "Lcl Rotation"
// properties within "Model: \"Mesh\"" sections, and moves their values to new "GeometricRotation" properties.
// It creates new files with "_fixed" appended to the original filename, preserving the original files.
//
// Usage: Place this executable in the same directory as your .fbx files and run it. It will process all .fbx files and output fixed versions.
// License: MIT License (https://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        string directory = AppContext.BaseDirectory;
        string[] fbxFiles = Directory.GetFiles(directory, "*.fbx");

        if (fbxFiles.Length == 0)
        {
            Console.WriteLine("No .fbx files found in the current directory.");
            return;
        }

        foreach (string filePath in fbxFiles)
        {
            // Skip already-fixed files
            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_fixed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping already-fixed file: {Path.GetFileName(filePath)}");
                continue;
            }

            Console.WriteLine($"Processing: {Path.GetFileName(filePath)}");

            // Check if the file is ASCII FBX (not binary)
            if (!IsAsciiFbx(filePath))
            {
                Console.WriteLine($"  Skipped: '{Path.GetFileName(filePath)}' is binary FBX, not ASCII.");
                continue;
            }

            try
            {
                List<string> lines = new List<string>(File.ReadAllLines(filePath));
                int rotationCount = 0;
                int scalingCount = 0;

                bool inModelSection = false;
                bool inProperties70 = false;
                int braceDepthModel = 0;
                int braceDepthProperties = 0;

                // Track the start index of the current Properties70 block
                int properties70StartIndex = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();

                    // Detect entering a Model: section
                    if (!inModelSection && trimmed.StartsWith("Model:") && trimmed.Contains("\"Mesh\""))
                    {
                        inModelSection = true;
                        braceDepthModel = 0;
                        braceDepthModel += CountChar(line, '{') - CountChar(line, '}');
                        continue;
                    }

                    if (inModelSection)
                    {
                        braceDepthModel += CountChar(line, '{') - CountChar(line, '}');

                        // Detect entering Properties70: within a Model section
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

                            // Check for Lcl Rotation
                            if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Rotation\""))
                            {
                                if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                        "Lcl Rotation", "GeometricRotation", "0,0,0"))
                                {
                                    rotationCount++;
                                }
                            }
                            // Check for Lcl Scaling
                            else if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Scaling\""))
                            {
                                if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                        "Lcl Scaling", "GeometricScaling", "1,1,1"))
                                {
                                    scalingCount++;
                                }
                            }

                            // Check if we've exited Properties70
                            if (braceDepthProperties <= 0)
                            {
                                inProperties70 = false;
                                properties70StartIndex = -1;
                            }
                        }

                        // Check if we've exited the Model section
                        if (braceDepthModel <= 0)
                        {
                            inModelSection = false;
                            inProperties70 = false;
                            properties70StartIndex = -1;
                        }
                    }
                }

                int totalModifications = rotationCount + scalingCount;

                if (totalModifications > 0)
                {
                    string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_fixed.fbx";
                    string outputPath = Path.Combine(Path.GetDirectoryName(filePath)!, outputFileName);
                    File.WriteAllLines(outputPath, lines);
                    Console.WriteLine($"  {rotationCount} rotation(s) and {scalingCount} scaling(s) moved to Geometric properties. Saved: {outputFileName}");
                }
                else
                {
                    Console.WriteLine("  No 'Lcl Rotation' or 'Lcl Scaling' entries found to modify.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error processing file: {ex.Message}");
            }
        }

        Console.WriteLine("\nDone. Press any key to exit.");
        Console.ReadKey();
    }

    /// <summary>
    /// Processes a Lcl property line (Rotation or Scaling), moving its values to the
    /// corresponding Geometric property and zeroing/resetting the original.
    /// </summary>
    /// <param name="lines">The full list of file lines.</param>
    /// <param name="currentIndex">Reference to the current line index (may shift on insert).</param>
    /// <param name="properties70StartIndex">The start index of the Properties70 block.</param>
    /// <param name="lclName">The Lcl property name, e.g. "Lcl Rotation" or "Lcl Scaling".</param>
    /// <param name="geometricName">The Geometric property name, e.g. "GeometricRotation" or "GeometricScaling".</param>
    /// <param name="defaultValues">The default/reset values, e.g. "0,0,0" for rotation or "1,1,1" for scaling.</param>
    /// <returns>True if a modification was made.</returns>
    static bool ProcessLclProperty(List<string> lines, ref int currentIndex, int properties70StartIndex,
        string lclName, string geometricName, string defaultValues)
    {
        string line = lines[currentIndex];
        string trimmed = line.TrimStart();

        // Build regex to match: P: "Lcl Rotation", "Lcl Rotation", "", "A+", X, Y, Z
        string escapedName = Regex.Escape(lclName);
        string pattern = $@"^P:\s*""{escapedName}""\s*,\s*""{escapedName}""\s*,\s*""[^""]*""\s*,\s*""([^""]*)""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$";

        Match match = Regex.Match(trimmed, pattern);
        if (!match.Success)
            return false;

        string aValue = match.Groups[1].Value;
        string x = match.Groups[2].Value.Trim();
        string y = match.Groups[3].Value.Trim();
        string z = match.Groups[4].Value.Trim();

        string indent = line.Substring(0, line.Length - trimmed.Length);

        // Search backwards for an existing Geometric line in this Properties70 block
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

        // Also search forwards in case the Geometric line is below the Lcl line
        if (existingGeoIndex < 0)
        {
            for (int j = currentIndex + 1; j < lines.Count; j++)
            {
                string checkTrimmed = lines[j].TrimStart();
                // Stop if we've left the Properties70 block (closing brace)
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
            // Update the existing Geometric line's values in place
            string geoIndent = lines[existingGeoIndex].Substring(0,
                lines[existingGeoIndex].Length - lines[existingGeoIndex].TrimStart().Length);
            lines[existingGeoIndex] = $"{geoIndent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
        }
        else
        {
            // Insert a new Geometric line above the Lcl line
            string geometricLine = $"{indent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
            lines.Insert(currentIndex, geometricLine);
            // The current Lcl line shifted down by one
            currentIndex++;
        }

        // Zero/reset the Lcl line
        lines[currentIndex] = $"{indent}P: \"{lclName}\", \"{lclName}\", \"\", \"{aValue}\",{defaultValues}";

        return true;
    }

    /// <summary>
    /// Checks whether an FBX file is ASCII format.
    /// Binary FBX files start with the magic bytes "Kaydara FBX Binary  \0".
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