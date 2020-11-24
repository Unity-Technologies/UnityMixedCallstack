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
        private static List<Range> _legacyRanges = new List<Range>();
        private static FuzzyRangeComparer _comparer = new FuzzyRangeComparer();
        private static bool _enabled;
        private static IVsOutputWindowPane _debugPane;
        private static Dictionary<int, PmipFile> _currentFiles = new Dictionary<int, PmipFile>();
        private static FileStream _fileStream;
        private static StreamReader _fileStreamReader;

        struct PmipFile
        {
            public int count;
            public string path;
        }

        public void OnLoadComplete(DkmProcess process, DkmWorkList workList, DkmEventDescriptor eventDescriptor)
        {
            DisposeStreams();

            if (_debugPane == null)
            {
                IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                Guid debugPaneGuid = VSConstants.GUID_OutWindowDebugPane;
                outWindow?.GetPane(ref debugPaneGuid, out _debugPane);
                if (_debugPane != null)
                    _debugPane.OutputString("MIXEDCALLSTACK WORKS NOW?!\n");
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

            try
            {
                DkmStackWalkFrame[] retVal = new[] { UnityMixedStackFrame(stackContext, input) };
                return retVal;
            } catch (Exception ex)
            {
                _debugPane?.OutputString("UNITYMIXEDCALLSTACK :: ip : " + input.Process.LivePart.Id + " threw exception: " + ex.Message + "\n" + ex.StackTrace);
            }
            return new[] { input };
        }

        private static DkmStackWalkFrame UnityMixedStackFrame(DkmStackContext stackContext, DkmStackWalkFrame frame)
        {
            RefreshStackData(frame.Process.LivePart.Id);
            _debugPane?.OutputString("UNITYMIXEDCALLSTACK :: done refreshing data\n");
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

            _rangesSortedByIp.Clear();
        }

        private static bool CheckForUpdatedFiles(FileInfo[] taskFiles)
        {
            bool retVal = false;
            try
            {
                foreach (FileInfo taskFile in taskFiles)
                {
                    string fName = Path.GetFileNameWithoutExtension(taskFile.Name);
                    string[] tokens = fName.Split('_');
                    PmipFile pmipFile = new PmipFile()
                    {
                        count = int.Parse(tokens[2]),
                        path = taskFile.FullName
                    };

                    _debugPane?.OutputString("MIXEDCALLSTACK :: parsing fName: " + fName + " Tokens length: "+ tokens.Length +"\n");

                    // 3 is legacy and treat everything as root domain
                    if (tokens.Length == 3 &&
                        (!_currentFiles.TryGetValue(0, out PmipFile curFile) ||
                        curFile.count < pmipFile.count))
                    {
                        _currentFiles[0] = pmipFile;
                        retVal = true;
                    }
                    else if (tokens.Length == 4)
                    {
                        int domainID = int.Parse(tokens[3]);
                        if (!_currentFiles.TryGetValue(domainID, out PmipFile cFile) || cFile.count < pmipFile.count)
                        {
                            _debugPane?.OutputString("MIXEDCALLSTACK :: adding pmip file to list: " + pmipFile.path + "\n");
                            _currentFiles[domainID] = pmipFile;
                            retVal = true;
                        }
                    }
                }
            } catch (Exception e)
            {
                _debugPane?.OutputString("MIXEDCALLSTACK :: BAD THINGS HAPPEND: " + e.Message + "\n");
                _enabled = false;
            }
            return retVal;
        }

        private static void RefreshStackData(int pid)
        {
            DirectoryInfo taskDirectory = new DirectoryInfo(Path.GetTempPath());
            FileInfo[] taskFiles = taskDirectory.GetFiles("pmip_" + pid + "_*.txt");

            _debugPane?.OutputString("MIXEDCALLSTACK :: taskfiles length: " + taskFiles.Length + "\n");

            if (taskFiles.Length < 1)
                return;

            if (!CheckForUpdatedFiles(taskFiles))
                return;

            foreach (PmipFile pmipFile in _currentFiles.Values)
            {

                _debugPane?.OutputString("MIXEDCALLSTACK :: Reading pmip file: " + pmipFile.path + "\n");
                DisposeStreams();
                try
                {
                    _fileStream = new FileStream(pmipFile.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    _fileStreamReader = new StreamReader(_fileStream);
                    var versionStr = _fileStreamReader.ReadLine();
                    const char delimiter = ':';
                    var tokens = versionStr.Split(delimiter);

                    if (tokens.Length != 2)
                        throw new Exception("Failed reading input file " + pmipFile.path + ": Incorrect format");

                    if (!double.TryParse(tokens[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var version))
                        throw new Exception("Failed reading input file " + pmipFile.path + ": Incorrect version format");

                    if (version > 2.0)
                        throw new Exception("Failed reading input file " + pmipFile.path + ": A newer version of UnityMixedCallstacks plugin is required to read this file");
                }
                catch (Exception ex)
                {
                    _debugPane?.OutputString("MIXEDCALLSTACK :: Unable to read dumped pmip file: " + ex.Message + "\n");
                    DisposeStreams();
                    _enabled = false;
                    return;
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
                    _debugPane?.OutputString("MIXEDCALLSTACK :: map now has " + _rangesSortedByIp.Count + " entries! legacy map has: "+ _legacyRanges.Count +"\n");
                }
                catch (Exception ex)
                {
                    _debugPane?.OutputString("MIXEDCALLSTACK :: Unable to read dumped pmip file: " + ex.Message + "\n");
                    DisposeStreams();
                    _enabled = false;
                    return;
                }
            }

            _legacyRanges.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
            _rangesSortedByIp.Sort((r1, r2) => r1.Start.CompareTo(r2.Start));
        }

        private static bool TryGetDescriptionForIp(ulong ip, out string name)
        {
            name = string.Empty;

            _debugPane?.OutputString("MIXEDCALLSTACK :: Looking for ip: " + String.Format("{0:X}", ip) + "\n");
            var rangeToFindIp = new Range() { Start = ip };
            var index = _rangesSortedByIp.BinarySearch(rangeToFindIp, _comparer);
            
            if (index >= 0)
            {
                _debugPane?.OutputString("MIXEDCALLSTACK :: SUCCESS!!\n");
                name = _rangesSortedByIp[index].Name;
                return true;
            }

            index = _legacyRanges.BinarySearch(rangeToFindIp, _comparer);
            if (index >= 0)
            {
                _debugPane?.OutputString("MIXEDCALLSTACK :: LEGACY SUCCESS!! "+ String.Format("{0:X}", _legacyRanges[index].Start) +" -- "+ String.Format("{0:X}", _legacyRanges[index].End) + "\n");
                name = _legacyRanges[index].Name;
                return true;
            }

            return false;
        }

        public void OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
        {
            if (moduleInstance.Name.Contains("mono-2.0") && moduleInstance.MinidumpInfoPart == null)
                _enabled = true;
        }
    }
}