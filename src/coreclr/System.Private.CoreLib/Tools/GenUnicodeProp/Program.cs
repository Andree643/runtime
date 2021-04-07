// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Unicode;

namespace GenUnicodeProp
{
    internal static class Program
    {
        internal static bool Verbose = false;
        internal static bool IncludeCasingData = false;

        private const string SOURCE_NAME = "CharUnicodeInfoData.cs";

        private static void Main(string[] args)
        {
            Verbose = args.Contains("-Verbose", StringComparer.OrdinalIgnoreCase);
            IncludeCasingData = args.Contains("-IncludeCasingData", StringComparer.OrdinalIgnoreCase);

            // First, read the data files and build up a list of all
            // assigned code points.

            Console.WriteLine("Reading Unicode data files...");

            _ = UnicodeData.GetData(0); // processes files

            Console.WriteLine("Finished.");
            Console.WriteLine();

            Console.WriteLine("Initializing maps...");
            Dictionary<CategoryCasingInfo, byte> categoryCasingMap = new Dictionary<CategoryCasingInfo, byte>();
            Dictionary<NumericGraphemeInfo, byte> numericGraphemeMap = new Dictionary<NumericGraphemeInfo, byte>();

            // Next, iterate though all assigned code points, populating
            // the category casing & numeric grapheme maps. Also put the
            // data into the the DataTable structure, which will compute
            // the tiered offset tables.

            DataTable categoryCasingTable = new DataTable();
            DataTable numericGraphemeTable = new DataTable();

            for (int i = 0; i <= 0x10_FFFF; i++)
            {
                CodePoint thisCodePoint = UnicodeData.GetData(i);

                CategoryCasingInfo categoryCasingInfo = new CategoryCasingInfo(thisCodePoint);
                if (!categoryCasingMap.TryGetValue(categoryCasingInfo, out byte cciValue))
                {
                    cciValue = (byte)categoryCasingMap.Count;
                    categoryCasingMap[categoryCasingInfo] = cciValue;
                }
                categoryCasingTable.AddData((uint)i, cciValue);

                NumericGraphemeInfo numericGraphemeInfo = new NumericGraphemeInfo(thisCodePoint);
                if (!numericGraphemeMap.TryGetValue(numericGraphemeInfo, out byte ngiValue))
                {
                    ngiValue = (byte)numericGraphemeMap.Count;
                    numericGraphemeMap[numericGraphemeInfo] = ngiValue;
                }
                numericGraphemeTable.AddData((uint)i, ngiValue);
            }

            // Did anything overflow?

            Console.WriteLine($"CategoryCasingMap contains {categoryCasingMap.Count} entries.");
            if (categoryCasingMap.Count > 256)
            {
                throw new Exception("CategoryCasingMap exceeds max count of 256 entries!");
            }

            Console.WriteLine($"NumericGraphemeMap contains {numericGraphemeMap.Count} entries.");
            if (numericGraphemeMap.Count > 256)
            {
                throw new Exception("NumericGraphemeMap exceeds max count of 256 entries!");
            }

            Console.WriteLine();

            // Choose default ratios for the data tables we'll be generating.

            TableLevels categoryCasingTableLevelBits = new TableLevels(5, 4);
            TableLevels numericGraphemeTableLevelBits = new TableLevels(5, 4);

            // Now generate the tables.

            categoryCasingTable.GenerateTable("CategoryCasingTable", categoryCasingTableLevelBits.Level2Bits, categoryCasingTableLevelBits.Level3Bits);
            numericGraphemeTable.GenerateTable("NumericGraphemeTable", numericGraphemeTableLevelBits.Level2Bits, numericGraphemeTableLevelBits.Level3Bits);

            // If you want to see if a different ratio would have better compression
            // statistics, uncomment the lines below and re-run the application.
            // categoryCasingTable.CalculateTableVariants();
            // numericGraphemeTable.CalculateTableVariants();

            // Now generate the C# source file.

            using (StreamWriter file = File.CreateText(SOURCE_NAME))
            {
                file.Write("// Licensed to the .NET Foundation under one or more agreements.\n");
                file.Write("// The .NET Foundation licenses this file to you under the MIT license.\n");

                file.Write("using System.Diagnostics;\n\n");

                file.Write("namespace System.Globalization\n");
                file.Write("{\n");
                file.Write("    public static partial class CharUnicodeInfo\n    {\n");

                file.Write("        // THE FOLLOWING DATA IS AUTO GENERATED BY GenUnicodeProp program UNDER THE TOOLS FOLDER\n");
                file.Write("        // PLEASE DON'T MODIFY BY HAND\n");

                PrintAssertTableLevelsBitCountRoutine("CategoryCasing", file, categoryCasingTableLevelBits);

                file.Write($"\n        // {categoryCasingTableLevelBits} index table of the Unicode category & casing data.");
                PrintSourceIndexArray("CategoryCasingLevel1Index", categoryCasingTable, file);

                file.Write("\n        // Contains Unicode category & bidi class information");
                PrintValueArray("CategoriesValues", categoryCasingMap, CategoryCasingInfo.ToCategoryBytes, file);

                if (IncludeCasingData)
                {
                    // Only write out the casing data if we have been asked to do so.

                    file.Write("\n        // Contains simple culture-invariant uppercase mappings");
                    PrintValueArray("UppercaseValues", categoryCasingMap, CategoryCasingInfo.ToUpperBytes, file);

                    file.Write("\n        // Contains simple culture-invariant lowercase mappings");
                    PrintValueArray("LowercaseValues", categoryCasingMap, CategoryCasingInfo.ToLowerBytes, file);

                    file.Write("\n        // Contains simple culture-invariant titlecase mappings");
                    PrintValueArray("TitlecaseValues", categoryCasingMap, CategoryCasingInfo.ToTitleBytes, file);

                    file.Write("\n        // Contains simple culture-invariant case fold mappings");
                    PrintValueArray("CaseFoldValues", categoryCasingMap, CategoryCasingInfo.ToCaseFoldBytes, file);
                }

                PrintAssertTableLevelsBitCountRoutine("NumericGrapheme", file, numericGraphemeTableLevelBits);

                file.Write($"\n        // {numericGraphemeTableLevelBits} index table of the Unicode numeric & text segmentation data.");
                PrintSourceIndexArray("NumericGraphemeLevel1Index", numericGraphemeTable, file);

                file.Write("\n        // Contains decimal digit values in high nibble; digit values in low nibble");
                PrintValueArray("DigitValues", numericGraphemeMap, NumericGraphemeInfo.ToDigitBytes, file);

                file.Write("\n        // Contains numeric values");
                PrintValueArray("NumericValues", numericGraphemeMap, NumericGraphemeInfo.ToNumericBytes, file);

                file.Write("\n        // Contains grapheme cluster segmentation values");
                PrintValueArray("GraphemeSegmentationValues", numericGraphemeMap, NumericGraphemeInfo.ToGraphemeBytes, file);

                file.Write("\n    }\n}\n");
            }

            // Quick fixup: Replace \n with \r\n on Windows.

            if (Environment.NewLine != "\n")
            {
                File.WriteAllText(SOURCE_NAME, File.ReadAllText(SOURCE_NAME).Replace("\n", Environment.NewLine));
            }

            Console.WriteLine("Completed!");
        }

        private static void PrintSourceIndexArray(string tableName, DataTable d, StreamWriter file)
        {
            Console.WriteLine("    ******************************** .");

            var levels = d.GetBytes();

            PrintByteArray(tableName, file, levels[0]);
            PrintByteArray(tableName.Replace('1', '2'), file, levels[1]);
            PrintByteArray(tableName.Replace('1', '3'), file, levels[2]);
        }

        private static void PrintValueArray<T>(string tableName, Dictionary<T, byte> d, Func<T, byte[]> getBytesCallback, StreamWriter file)
        {
            Console.WriteLine("    ******************************** .");

            // Create reverse mapping of byte -> T,
            // then dump each T to the response (as binary).

            byte highestByteSeen = 0;
            Dictionary<byte, T> reverseMap = new Dictionary<byte, T>();
            foreach (var entry in d)
            {
                reverseMap.Add(entry.Value, entry.Key);
                if (entry.Value > highestByteSeen)
                {
                    highestByteSeen = entry.Value;
                }
            }

            List<byte> binaryOutput = new List<byte>();
            for (int i = 0; i <= highestByteSeen; i++)
            {
                binaryOutput.AddRange(getBytesCallback(reverseMap[(byte)i]));
            }

            PrintByteArray(tableName, file, binaryOutput.ToArray());
        }

        private static void PrintByteArray(string tableName, StreamWriter file, byte[] str)
        {
            file.Write("\n        private static ReadOnlySpan<byte> " + tableName + " => new byte[" + str.Length + "]\n        {\n");
            file.Write("            0x{0:x2}", str[0]);
            for (var i = 1; i < str.Length; i++)
            {
                file.Write(i % 16 == 0 ? ",\n            " : ", ");
                file.Write("0x{0:x2}", str[i]);
            }
            file.Write("\n        };\n");
        }

        private static void PrintAssertTableLevelsBitCountRoutine(string tableName, StreamWriter file, TableLevels expectedLevels)
        {
            file.Write("\n");
            file.Write("        [Conditional(\"DEBUG\")]\n");
            file.Write($"        private static void Assert{tableName}TableLevels(int level1BitCount, int level2BitCount, int level3BitCount)\n");
            file.Write("        {\n");
            file.Write("            // Ensures that the caller expects the same L1:L2:L3 count as the actual backing data.\n");
            file.Write($"            Debug.Assert(level1BitCount == {expectedLevels.Level1Bits}, \"Unexpected level 1 bit count.\");\n");
            file.Write($"            Debug.Assert(level2BitCount == {expectedLevels.Level2Bits}, \"Unexpected level 2 bit count.\");\n");
            file.Write($"            Debug.Assert(level3BitCount == {expectedLevels.Level3Bits}, \"Unexpected level 3 bit count.\");\n");
            file.Write("        }\n");
        }
    }
}