# Unity Mixed Callstack Visual Studio Plugin

> [!NOTE]
> This fork of PmipMyCallstack has been developed for developers at Unity and support has been added to [our clone of Mono](https://github.com/Unity-Technologies/mono).

## Summary

UnityMixedCallstack is a Visual Studio 2015/2017/2019/2022 extension to help debug Unity native code. Mono doesn't generate debug symbols that Visual Studio understands for JITted functions. As a result, Visual Studio cannot show anything meaningful for managed stack frames. The resulting call stack is confusing: native functions are mixed with what appear to be random numbers, which can only be translated into managed functions one at a time with the `mono_pmip` function. This plugin translates these managed addresses into descriptive stack frames for you when you use the 4.6 Mono runtime.

> [!IMPORTANT]
> This plugin does have an effect on performance when enabled via diagnostic switch, as it records the address of each function as they get JITted. This is most noticeable on startup and when first entering Play mode.

Before:

![Before Unity Mixed Callstack](https://raw.githubusercontent.com/mderoy/UnityMixedCallstack/master/Images/csb.png)

After:
![After Unity Mixed Callstack](https://raw.githubusercontent.com/mderoy/UnityMixedCallstack/master/Images/cs.png)

## Get started

### Prerequisites
    
* You must be using the 4.6 Mono Runtime.
* Only available for Unity 2018.2 and later.
* Only works with internal source builds of the Editor.
* Older versions of Visual Studio require an older version of the plugin. Version 2.6.1 was the last version with working support for Visual Studio 2019.

### Build from source

> [!NOTE]
> You may need to install the [Visual Studio SDK](https://msdn.microsoft.com/en-us/library/mt683786.aspx).

To ensure you are using the latest version of the plugin, you can build from source. To build from source, clone this repository with Git and open the UnityMixedCallstack solution. Simply select **Release** and build like you would any other Visual Studio file. You'll find the installation files in `bin/Release`. 

### Download precompiled

A precompiled plugin can be found on the [releases page](https://github.com/Unity-Technologies/UnityMixedCallstack/releases).

### Install the plugin

To install the plugin, double click UnityMixedCallStack.vsix (open with Visual Studio version selector) which will open the VSIX installer. Then select the Visual Studio versions you want to install the plugin to and hit install. You'll need to close all instances of Visual Studio before you install.

### Enable the plugin in Unity

The plugin can be enabled via a diagnostic switch in the Editor.

Select **Edit > Preferences** to open the Unity Preferences window. Select the Diagnostics tab on the left, and then select the **EnableMixedCallstackDebugging** option. You'll need to restart the Editor after setting this option in order for it to take effect.

> [!NOTE]
> On newer Unity versions (2023) the diagnostics switches are grouped into sub-sections and the setting can be found in the **Core** section.

### Disable the **Enable Just My Code** setting in Visual Studio

Make sure that the **Tools > Options > Debugging > Enable Just My Code** option is disabled. Otherwise the callstack will display the managed frames as a single frame label `[External Code]`.

### Use the 4.6 Mono Runtime

> [!NOTE]
> This does not seem to be needed anymore on recent versions of Unity (2023)

This plugin will only emit managed stack frames when using the 4.6 mono runtime in your Unity project. To set this, go to **Edit > Project Settings > Player** and change the **Scripting Runtime Version** value to **Experimental (.NET 4.6 Equivalent)**. You will be asked to restart the Editor, after which the project will open with the new runtime. To force the Editor to use the 4.6 runtime, you can launch the Editor with the arguments `-scripting-runtime-version Latest`

## Credits and technical details

Michael DeRoy wrote this plug with help from Jonathan Chambers; it was forked off of JB Evain (VSTU)'s PMIPMyCallstack plugin, which would call mono_pmip from Visual Studio on each managed frame. The original plugin worked well for small programs, but due to the high number of inter process function calls per frame Unity would lag at every breakpoint and hang for minutes if the threads window was opened. This new plugin relies on the mono runtime to write out the address of each managed function as they are JITted.

This gets output into `AppData/Local/Temp` as a `pmip_<processid>_<numdomainreload>` file. These files are created in such a way that they will automatically delete themselves when no one holds a handle to them, so don't worry about files building up! `numdomainreload` is a number that increments every domain reload (since we reJIT functions). The file is formatted like below. Following a plugin version line (to deal with possible future updates), each line shows the start and end address of the managed function, followed by a delimiter, followed by a string to be displayed in Visual Studio:

```
UnityMixedCallstacks:1.0
000000013FC9BAC0;000000013FC9C004;[mscorlib.dll] System.Reflection.MonoMethod:GetPseudoCustomAttributes ()
000000013FC9C030;000000013FC9C0BE;[UnityEngine.CoreModule.dll] UnityEngine.RuntimeInitializeOnLoadMethodAttribute:get_loadType ()
000000013FC9C0D0;000000013FC9C1BB;[mscorlib.dll] System.Reflection.MonoMethod:get_Name ()
000000013FC9C1E0;000000013FC9C42B;[mscorlib.dll] (wrapper managed-to-native) System.Reflection.MonoMethod:get_name (System.Reflection.MethodBase)
000000013FC9C440;000000013FC9C4DC;[mscorlib.dll] System.Collections.Generic.List`1&lt;UnityEngine.RuntimeInitializeLoadType>:.cctor ()
000000013FC9C4F0;000000013FC9C5B4;[mscorlib.dll] System.Collections.Generic.List`1&lt;UnityEngine.RuntimeInitializeLoadType>:.ctor ()
000000013FC9C5D0;000000013FC9C74B;[mscorlib.dll] System.Collections.Generic.List`1&lt;UnityEngine.RuntimeInitializeLoadType>:Add (UnityEngine.RuntimeInitializeLoadType)
000000013FC9C770;000000013FC9C93E;[mscorlib.dll] System.Collections.Generic.List`1&lt;UnityEngine.RuntimeInitializeLoadType>:EnsureCapacity (int)
```

The Visual Studio plugin itself is fairly simple. We open a filestream on the pmip file, and as we read it we load the items into a `List` sorted by function address. If domain reload is triggered, the list is invalidated, and we open the most recent pmip file mono produced. We implement a `FilterNextFrame` function that Visual Studio will call back into when hitting a breakpoint. The plugin will then analyze each frame, and for managed frames it will check that the address of the function is in our sorted list via a fuzzy binary search. If the function is found, instead of writing the address of the function out, we write out the description of the function that we found.

## Advanced options

The diagnostic switch has not been configured to enable the plugin when you build standalone players. You can set the environment variable `UNITY_MIXED_CALLSTACK` to enable the plugin.

> [!WARNING]
> **DO NOT DO THIS GLOBALLY** as this will activate the plugin for every Mono used in Unity **including the ones used to build Unity**, so unless you want your next build to take hours **do not do this** in the same shells you're building in.
