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
using UnityMixedCallStack;

namespace UnityMixedCallstack
{
    public class UnityMixedCallstackFilter : IDkmCallStackFilter, IDkmLoadCompleteNotification, IDkmModuleInstanceLoadNotification
    {
        private static bool _enabled;
        public static IVsOutputWindowPane _debugPane;
        private static Dictionary<int, PmipFile> _currentFiles = new Dictionary<int, PmipFile>();

        struct PmipFile
        {
            public int count;
            public string path;
        }

        public void OnLoadComplete(DkmProcess process, DkmWorkList workList, DkmEventDescriptor eventDescriptor)
        {
            PmipReader.DisposeStreams();

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
#if DEBUG
            _debugPane?.OutputString($"UNITYMIXEDCALLSTACK :: done refreshing data :: looking for address: {frame.InstructionAddress.CPUInstructionPart.InstructionPointer:X16}\n");
#endif

            string name = null;
            if (PmipReader.TryGetDescriptionForIp(frame.InstructionAddress.CPUInstructionPart.InstructionPointer, out name))
            {
#if DEBUG
                _debugPane?.OutputString($"ip: {frame.InstructionAddress.CPUInstructionPart.InstructionPointer:X16} ### {name}\n");
#endif
                return DkmStackWalkFrame.Create(
                    stackContext.Thread,
                    frame.InstructionAddress,
                    frame.FrameBase,
                    frame.FrameSize,
                    frame.Flags,
                    name,
                    frame.Registers,
                    frame.Annotations);
            }
#if DEBUG
            else
                _debugPane?.OutputString($"IP not found: {frame.InstructionAddress.CPUInstructionPart.InstructionPointer:X16}\n");
#endif

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
                            _currentFiles[domainID] = pmipFile;
                            retVal = true;
                        }
                    }
                }
            } catch (Exception e)
            {
                _debugPane?.OutputString("MIXEDCALLSTACK :: Exception thrown during CheckForUpdatedFiles: " + e.Message + "\n");
                _enabled = false;
            }
            return retVal;
        }

        private static void RefreshStackData(int pid)
        {
            DirectoryInfo taskDirectory = new DirectoryInfo(Path.GetTempPath());
            FileInfo[] taskFiles = taskDirectory.GetFiles("pmip_" + pid + "_*.txt");

            if (taskFiles.Length < 1)
                return;

            if (CheckForUpdatedFiles(taskFiles))
                PmipReader.DisposeStreams();

            foreach (PmipFile pmipFile in _currentFiles.Values)
            {

                _enabled = PmipReader.ReadPmipFile(pmipFile.path);
                if (!_enabled)
                {
                    _debugPane?.OutputString($"Unable to read file: {pmipFile.path}\n");
                }
            }

            PmipReader.Sort();
        }

        public void OnModuleInstanceLoad(DkmModuleInstance moduleInstance, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
        {
            if (moduleInstance.Name.Contains("mono-2.0") && moduleInstance.MinidumpInfoPart == null)
                _enabled = true;
        }
    }
}