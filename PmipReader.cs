

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityMixedCallstack;

namespace UnityMixedCallStack
{
	public class PmipReader
	{
		private static List<Range> _rangesSortedByIp = new List<Range>();
		private static List<Range> _legacyRanges = new List<Range>();
		private static FuzzyRangeComparer _comparer = new FuzzyRangeComparer();
		private static FileStream _fileStream;
		private static StreamReader _fileStreamReader;

		public static void Sort()
		{
			_legacyRanges.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
			_rangesSortedByIp.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
		}

		public static void DisposeStreams()
		{
			_fileStreamReader?.Dispose();
			_fileStreamReader = null;

			_fileStream?.Dispose();
			_fileStream = null;

			_rangesSortedByIp.Clear();
		}
		public static bool ReadPmipFile(string filePath)
		{
			//_debugPane?.OutputString("MIXEDCALLSTACK :: Reading pmip file: " + filePath + "\n");
			DisposeStreams();
			try
			{
				_fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				_fileStreamReader = new StreamReader(_fileStream);
				var versionStr = _fileStreamReader.ReadLine();
				const char delimiter = ':';
				var tokens = versionStr.Split(delimiter);

				if (tokens.Length != 2)
					throw new Exception("Failed reading input file " + filePath + ": Incorrect format");

				if (!double.TryParse(tokens[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var version))
					throw new Exception("Failed reading input file " + filePath + ": Incorrect version format");

				if (version > 2.0)
					throw new Exception("Failed reading input file " + filePath + ": A newer version of UnityMixedCallstacks plugin is required to read this file");
			}
			catch (Exception ex)
			{
				//_debugPane?.OutputString("MIXEDCALLSTACK :: Unable to read dumped pmip file: " + ex.Message + "\n");
				DisposeStreams();
				return false;
			}

			try
			{
				string line;
				while ((line = _fileStreamReader.ReadLine()) != null)
				{
					const char delemiter = ';';
					var tokens = line.Split(delemiter);

					//should never happen, but lets be safe and not get array out of bounds if it does
					if (tokens.Length == 3 || tokens.Length == 4)
					{
						string startip = tokens[0];
						string endip = tokens[1];
						string description = tokens[2];
						string file = "";
						if (tokens.Length == 4)
							file = tokens[3];

						if (startip.StartsWith("---"))
						{
							startip = startip.Remove(0, 3);
						}

						var startiplong = ulong.Parse(startip, NumberStyles.HexNumber);
						var endipint = ulong.Parse(endip, NumberStyles.HexNumber);
						if (tokens[0].StartsWith("---"))
						{
							// legacy stored in new pmip file
							_legacyRanges.Add(new Range() { Name = description, File = file, Start = startiplong, End = endipint });
						}
						else
							_rangesSortedByIp.Add(new Range() { Name = description, File = file, Start = startiplong, End = endipint });
					}
				}
				//_debugPane?.OutputString("MIXEDCALLSTACK :: map now has " + _rangesSortedByIp.Count + " entries! legacy map has: " + _legacyRanges.Count + "\n");
			}
			catch (Exception ex)
			{
				//_debugPane?.OutputString("MIXEDCALLSTACK :: Unable to read dumped pmip file: " + ex.Message + "\n");
				DisposeStreams();
				return false;
			}
			return true;
		}

		public static bool TryGetDescriptionForIp(ulong ip, out string name)
		{
			name = string.Empty;

			//_debugPane?.OutputString("MIXEDCALLSTACK :: Looking for ip: " + String.Format("{0:X}", ip) + "\n");
			var rangeToFindIp = new Range() { Start = ip };
			var index = _rangesSortedByIp.BinarySearch(rangeToFindIp, _comparer);

			if (index >= 0)
			{
				//_debugPane?.OutputString("MIXEDCALLSTACK :: SUCCESS!!\n");
				name = _rangesSortedByIp[index].Name;
				return true;
			}

			index = _legacyRanges.BinarySearch(rangeToFindIp, _comparer);
			if (index >= 0)
			{
				//_debugPane?.OutputString("MIXEDCALLSTACK :: LEGACY SUCCESS!! "+ String.Format("{0:X}", _legacyRanges[index].Start) +" -- "+ String.Format("{0:X}", _legacyRanges[index].End) + "\n");
				name = _legacyRanges[index].Name;
				return true;
			}

			return false;
		}

	}
}
