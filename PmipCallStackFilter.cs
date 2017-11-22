using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;

namespace PmipMyCallStack
{
    public class PmipCallStackFilter : IDkmCallStackFilter
    {
        private static List<Range> _rangesSortedByIp = new List<Range>();
        private static FuzzyRangeComparer _comparer = new FuzzyRangeComparer();

        private static string _currentFile;
        private static FileStream _fileStream;
        private static StreamReader _fileStreamReader;

        public DkmStackWalkFrame[] FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            if (input == null) // after last frame
                return null;

            if (input.InstructionAddress == null) // error case
                return new[] { input };

            if (input.InstructionAddress.ModuleInstance != null && input.InstructionAddress.ModuleInstance.Module != null) // code in existing module
                return new[] { input };

            if (!stackContext.Thread.IsMainThread) // error case
                return new[] { input };


            return new[] { PmipStackFrame(stackContext, input) };
        }

        public static DkmStackWalkFrame PmipStackFrame(DkmStackContext stackContext, DkmStackWalkFrame frame)
        {
            RefreshStackData(frame.Process.LivePart.Id);
            string name = null;
            if (TryGetDescriptionForIp(frame.InstructionAddress.CPUInstructionPart.InstructionPointer, out name))
                return DkmStackWalkFrame.Create(
                    stackContext.Thread,
                    frame.InstructionAddress,
                    frame.FrameBase,
                    frame.FrameSize,
                    frame.Flags,
                    name,
                    frame.Registers,
                    frame.Annotations);

            return frame;
        }

        private static int GetFileNameSequenceNum(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            const char delemiter = '_';
            var tokens = name.Split(delemiter);

            if (tokens.Length != 3)
                return -1;

            return int.Parse(tokens[2]);
        }

        public static void RefreshStackData(int pid)
        {
            DirectoryInfo taskDirectory = new DirectoryInfo(Path.GetTempPath());
            FileInfo[] taskFiles = taskDirectory.GetFiles("pmip_" + pid + "_*.txt");

            if (taskFiles.Length < 1)
                return;

            Array.Sort(taskFiles, (a, b) => GetFileNameSequenceNum(a.Name).CompareTo(GetFileNameSequenceNum(b.Name)));
            var fileName = taskFiles[taskFiles.Length - 1].FullName;

            if (_currentFile != fileName)
            {
                _fileStreamReader?.Dispose();
                _fileStream?.Dispose();
                _rangesSortedByIp.Clear();

                try
                {
                    _fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    _fileStreamReader = new StreamReader(_fileStream);
                    _currentFile = fileName;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Unable to read dumped pmip file: " + ex.Message);
                }
            }

            try
            {
                string line;
                while ((line = _fileStreamReader.ReadLine()) != null)
                {
                    const char delemiter = ';';
                    var tokens = line.Split(delemiter);

                    //should never happen, but lets be safe and not get array out of bounds if it does
                    if (tokens.Length != 3)
                        continue;

                    var startip = tokens[0];
                    var endip = tokens[1];
                    var description = tokens[2];

                    var startiplong = ulong.Parse(startip, NumberStyles.HexNumber);
                    var endipint = ulong.Parse(endip, NumberStyles.HexNumber);
                    _rangesSortedByIp.Add(new Range() { Name = description, Start = startiplong, End = endipint });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unable to read dumped pmip file: " + ex.Message);
            }

            _rangesSortedByIp.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
        }

        public static bool TryGetDescriptionForIp(ulong ip, out string name)
        {
            name = string.Empty;

            var rangeToFindIp = new Range() { Start = ip };
            var index = _rangesSortedByIp.BinarySearch(rangeToFindIp, _comparer);

            if (index < 0)
                return false;

            name = _rangesSortedByIp[index].Name;
            return true;
        }
    }
}