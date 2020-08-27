using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace UnityMixedCallstack
{
    public class UnityMixedCallstackFilter : IDkmCallStackFilter, IDkmLoadCompleteNotification, IDkmModuleInstanceLoadNotification
    {
        private static List<Range> _rangesSortedByIp = new List<Range>();
        private static FuzzyRangeComparer _comparer = new FuzzyRangeComparer();
        private static bool _enabled;
        private static IVsOutputWindowPane _debugPane;
        private static string _currentFile;
        private static FileStream _fileStream;
        private static StreamReader _fileStreamReader;

        public void OnLoadComplete(DkmProcess process, DkmWorkList workList, DkmEventDescriptor eventDescriptor)
        {
            DisposeStreams();

            if (_debugPane == null)
            {
                IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                Guid debugPaneGuid = VSConstants.GUID_OutWindowDebugPane;
                outWindow?.GetPane(ref debugPaneGuid, out _debugPane);
            }
        }

        public DkmStackWalkFrame[] FilterNextFrame(DkmStackContext stackContext, DkmStackWalkFrame input)
        {
            if (input == null) // after last frame
                return null;

            if (input.InstructionAddress == null) // error case
                return new[] { input };

            if (input.InstructionAddress.ModuleInstance != null && input.InstructionAddress.ModuleInstance.Module != null) // code in existing module
                return new[] { input };

            if (!_enabled) // environment variable not set
                return new[] { input };

            return new[] { UnityMixedStackFrame(stackContext, input) };
        }

        private static DkmStackWalkFrame UnityMixedStackFrame(DkmStackContext stackContext, DkmStackWalkFrame frame)
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

        private static void DisposeStreams()
        {
            _fileStreamReader?.Dispose();
            _fileStreamReader = null;

            _fileStream?.Dispose();
            _fileStream = null;

            _currentFile = null;

            _rangesSortedByIp.Clear();
        }

        private static void RefreshStackData(int pid)
        {
            DirectoryInfo taskDirectory = new DirectoryInfo(Path.GetTempPath());
            FileInfo[] taskFiles = taskDirectory.GetFiles("pmip_" + pid + "_*.txt");

            if (taskFiles.Length < 1)
                return;

            Array.Sort(taskFiles, (a, b) => GetFileNameSequenceNum(a.Name).CompareTo(GetFileNameSequenceNum(b.Name)));
            var fileName = taskFiles[taskFiles.Length - 1].FullName;

            if (_currentFile != fileName)
            {
                DisposeStreams();
                try
                {
                    _fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    _fileStreamReader = new StreamReader(_fileStream);
                    _currentFile = fileName;
                    var versionStr = _fileStreamReader.ReadLine();
                    const char delimiter = ':';
                    var tokens = versionStr.Split(delimiter);

                    if (tokens.Length != 2)
                        throw new Exception("Failed reading input file " + fileName + ": Incorrect format");

                    if (!double.TryParse(tokens[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var version))
                        throw new Exception("Failed reading input file " + fileName + ": Incorrect version format");

                    if(version > 1.0)
                        throw new Exception("Failed reading input file " + fileName + ": A newer version of UnityMixedCallstacks plugin is required to read this file");
                }
                catch (Exception ex)
                {
                    _debugPane?.OutputString("Unable to read dumped pmip file: " + ex.Message + "\n");
                    DisposeStreams();
                    _enabled = false;
                    return;
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
                _debugPane?.OutputString("Unable to read dumped pmip file: " + ex.Message + "\n");
                DisposeStreams();
                _enabled = false;
                return;
            }

            _rangesSortedByIp.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
        }

        private static bool TryGetDescriptionForIp(ulong ip, out string name)
        {
            name = string.Empty;

            var rangeToFindIp = new Range() { Start = ip };
            var index = _rangesSortedByIp.BinarySearch(rangeToFindIp, _comparer);

            if (index < 0)
                return false;

            name = _rangesSortedByIp[index].Name;
            return true;
        }

        public void OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
        {
            if (moduleInstance.Name.Contains("mono-2.0") && moduleInstance.MinidumpInfoPart == null)
                _enabled = true;
        }
    }
}